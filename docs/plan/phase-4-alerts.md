# Phase 4 — Alerts · 0.9 days

> **Where this builds, changed in Phase 2.** Alerts was a fourth module with its own five projects. It is now
> a **feature area inside Portfolio**: `Portfolio.Domain/Alerts/`, `Portfolio.Application/Alerts/`,
> `Portfolio.Api/`. Every requirement below is unchanged — thresholds, windows, cooldowns, evaluation after
> each poll, history, simulate, SSE, the ticket handshake, the `/api/alerts/*` URL space. What changed is
> which assemblies the files land in, and that three seams stop being module boundaries.
>
> The reasoning is in [00-overview.md](00-overview.md) §"Three modules, not four": `Ticker` meant a symbol in
> Portfolio, MarketData *and* Alerts, so the ubiquitous language never diverged and there was no second
> bounded context. Portfolio-with-alerts is the **core** subdomain; Identity is generic; MarketData is
> supporting.
>
> **Read this file with three substitutions:**
>
> | Written as | Build it as |
> |---|---|
> | `Alerts.Domain` / `Alerts.Application` / `Alerts.Api` | `Portfolio.Domain/Alerts/` and the matching folders |
> | `alerts.alert_settings`, `alerts.fired_alerts`, `alerts_svc` | `portfolio.alert_settings`, `portfolio.fired_alerts`, `portfolio_svc`, in `PortfolioDbContext` |
> | *"ask Portfolio which users hold this ticker"* via `IHoldersOfTicker` | an ordinary query inside Portfolio; the contract interface is still there but no boundary is crossed |
>
> Two further consequences, called out where they occur: `HoldingRemoved` no longer exists (§2.3), and the
> entity snippets' `UserId` is `Identity.Domain`'s type, which Portfolio may not reference — it is a plain
> `Guid`, exactly as `phase-2-implementation.md` §2.1 settled for `Holding`.
>
> Redis key prefixes stay `alerts:*`. They name the feature, not the module.

## 1. Goal

Set a threshold, click **Simulate** → an alert appears in the panel in under a second. Open `/notifications` → recent alerts are listed. Nudge a price past the threshold → a real, evaluation-driven alert fires.

This is P1 req 9 and, per §1 of the brief, one of the things being assessed. `Initial.md:200` says "protect time for it" — this plan protects it by scheduling it fourth of six rather than eighth of nine.

**What this phase deliberately does not build.** `Initial.md:134-136` designs cursor-based replay: alerts persisted with an orderable sequence, `Last-Event-ID` on reconnect, the last 24 hours on a fresh connect, "since recovered" rendering. None of that is in req 9, which asks for an event when the threshold is breached, threshold checking by a background service, and a manual simulate button — and nothing about persistence, offline delivery or catching up. It is dropped.

What survives is smaller and does the same job for the user: alerts are written to Postgres, the panel loads recent ones with a plain `GET` when it mounts, and SSE only ever pushes new ones. Reconnect after a blip and the panel refetches its history like any other query. No protocol, no cursor.

---

## 2. Backend

### 2.0 Resolve the ownership contradiction first

`Initial.md:22` says **Identity** owns "user preferences including alert settings" and, one sentence later, that **Alerts** owns "thresholds". Both cannot be true, and the answer is load-bearing: `:32` and `:34` claim *"Identity has zero inbound runtime coupling"*, which is the flagship argument of the whole document. But `:124` says evaluation runs for users "online or not", and offline users have no JWT in flight — so the evaluator must read each threshold from somewhere.

**Resolution: the alerts feature owns thresholds, windows and enabled-state.** Identity keeps account data and UI preferences only. Evaluation then needs no runtime call into Identity and `:34` survives intact.

Since Phase 2 that ownership sits in **Portfolio**, which strengthens the resolution rather than changing it: the evaluator reads holdings and thresholds from the same `DbContext`, and Identity still has zero inbound runtime coupling.

Update `docs/Initial.md:22` to match. Shipping a design doc that contradicts itself is worse than the contradiction.

### 2.1 `AlertSettings` — `Portfolio.Domain/Alerts/`

```csharp
public sealed class AlertSettings
{
    // The only constructor: every mapped value, assign and nothing else.
    private AlertSettings(
        AlertSettingsId id, UserId userId, bool enabled,
        decimal thresholdPercent, TimeSpan window);

    public AlertSettingsId Id { get; private set; }
    public UserId UserId { get; private set; }
    public bool Enabled { get; private set; }
    public decimal ThresholdPercent { get; private set; }   // 0.1 .. 50
    public TimeSpan Window { get; private set; }            // 1 min .. 1 hour

    public static OneOf<AlertSettings, InvalidInput> Create(UserId userId);
    public OneOf<Success, InvalidInput> Update(bool enabled, decimal thresholdPercent, TimeSpan window);
}
```

No base class, and the entity declares its own `Id` — see `phase-1-implementation.md` §5.2. `UserId` in both snippets is a plain `Guid`: `Identity.Domain`'s `UserId` is not reachable from Portfolio, and `phase-2-implementation.md` §2.1 already settled this for `Holding`.

One threshold and one window **per user**, applied to every ticker they hold. Not a per-ticker rules engine — the brief describes a single user-configured threshold, and a rules table would be a richer product than was asked for.

The window is capped at 1 hour: "moved sharply" is a minutes-to-an-hour concept, and a move over several days is a trend, which is a different feature. `Update` also rejects a window longer than the price-window retention from Phase 3, so a user cannot configure something the store cannot answer.

### 2.2 `FiredAlert` — history, not a cursor · `Portfolio.Domain/Alerts/`

```csharp
public sealed class FiredAlert
{
    private FiredAlert(
        FiredAlertId id, UserId userId, Ticker ticker, AlertDirection direction,
        decimal changePercent, Money triggerPrice, Money referencePrice,
        DateTimeOffset firedAt, bool isSimulated);

    public FiredAlertId Id { get; private set; }
    public UserId UserId { get; private set; }
    public Ticker Ticker { get; private set; }
    public AlertDirection Direction { get; private set; }   // Drawdown | RunUp
    public decimal ChangePercent { get; private set; }
    public Money TriggerPrice { get; private set; }         // the price that fired it
    public Money ReferencePrice { get; private set; }       // the window extreme it was measured against
    public DateTimeOffset FiredAt { get; private set; }
    public bool IsSimulated { get; private set; }
}
```

No `Sequence` column — that existed only to be an SSE cursor. History is ordered by `FiredAt DESC` and read through one index.

Both prices are kept because they make the alert text specific: *"AAPL fell 6.2% to $141.30, from a window high of $150.60."* Vague alerts are the reason people turn alerts off.

A price alert is **a moment that passed, not a condition that persists** (`Initial.md:138`, and it is the best line in the document). So the panel is titled *recent activity*, not *active alerts*, and rows carry a timestamp rather than implying the move is still happening.

### 2.3 Evaluation

Runs **immediately after each fetch, in the same cycle** — the natural trigger for "did this move sharply" is "a new price just arrived". Evaluating on any other schedule means re-checking data you already checked, or checking stale data.

Per ticker, compute **current, min, max** once from the Redis window. Look up which users hold that ticker, then test each of their thresholds against the same three numbers. That lookup used to be a cross-module call through `IHoldersOfTicker`; since Phase 2 it is an ordinary query inside Portfolio, against the same `DbContext` that holds the thresholds.

#### ⚠️ Fix the false positive

`Initial.md:128` presents this as the design's showcase:

> A price that falls from $150 to $141 and recovers to $149 reads as −0.7% on an endpoint comparison — invisible — but **+5.7% against the window's low**.

The arithmetic is right (150→149 = −0.67%; 149 vs 141 = +5.67%). The conclusion is not. In that scenario the holding is **down 0.7% over the hour**, and the system fires a **run-up** alert claiming +5.7%. Worse, `:126` makes it systematic: any ticker oscillating inside a band wider than the threshold fires every cycle, forever, gated only by the cooldown. That is a standing property of the window being reported as an event.

It is also the endpoint comparison that *«% за проміжок часу»* in req 9 literally describes. Three viable constraints, and you pick one in §7:

**Recency** — the extreme must be within the last *N* samples. Catches "fell hard just now"; misses a slow grind away from an old extreme.

**Current is the extreme** — only fire at new window highs and lows. Very quiet and very defensible, but gives up the partial-reversal case that min/max was added for.

**Sign agreement** — the endpoint delta and the extreme delta must agree in direction. Kills the oscillation loop, keeps genuine sharp moves, and is closest to the requirement's wording.

Whichever you pick, name the comparison in the alert text and put both deltas in the payload.

#### Guards, then cooldown

Three guards run before any comparison. There must be **enough samples** in the window, because a single stale point is not a window. Both ends must be **in the same trading session**, because a Friday-close-to-Monday-open gap is not a sharp move. And a **stale feed suppresses price alerts entirely**, raising a feed-health signal instead — *no new data must never read as "nothing moved."*

Then cooldown, in Redis:

```
SET alerts:cooldown:{userId}:{ticker}:{direction} 1 NX EX {cooldownSeconds}
```

If the `SET` fails the alert is suppressed. Expiry *is* the semantics of a cooldown, so a store with native expiry is the right one — a table would need a cleanup job to do the same thing worse. Per user, ticker **and** direction, so a drawdown cooldown does not mask a subsequent run-up.

Removing a holding clears any cooldown for that user and ticker, in **both** directions — `HoldingRemoved` carried no direction and neither does the delete. This was going to be Phase 2's `HoldingRemoved` domain event, consumed by Alerts across the module boundary; with Alerts inside Portfolio it is a call at the end of `RemoveHoldingCommandHandler`, and the domain-event infrastructure was deleted rather than built. It was the only event in the whole project, and it existed only to talk across a boundary that should not have been there. See [00-overview.md](00-overview.md) §"Three modules, not four".

### 2.4 Delivery

Write to Postgres, then publish. Connection state only decides whether it also arrives right now.

```
evaluate → INSERT portfolio.fired_alerts → PUBLISH alerts:user:{userId} {payload}
                                          ↓
                          every replica subscribes; the one holding
                          this user's stream writes it to the browser
```

Redis pub/sub fan-out is **mandatory**, not optional — an alert can be generated on replica A while the user's stream is held by replica B. It is about 20 lines. Without it, alerts silently never arrive for half your users the moment `maxReplicas` exceeds 1.

Persisting first is what makes a failed publish cheap: the row is there, and the panel picks it up on its next history fetch. Nothing is lost, nothing needs replaying.

### 2.5 The SSE endpoint

.NET 10 shipped first-class SSE — `TypedResults.ServerSentEvents(IAsyncEnumerable<SseItem<T>>)`.

```csharp
group.MapGet("/stream", (HttpContext ctx, IAlertStream stream, CancellationToken ct) =>
{
    var userId = ctx.GetTicketUserId();

    ctx.Response.Headers["Cache-Control"]     = "no-cache, no-transform";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";        // Envoy ignores it; nginx doesn't

    return TypedResults.ServerSentEvents(Live(userId, ct));
});

static async IAsyncEnumerable<SseItem<AlertDto?>> Live(
    UserId userId, [EnumeratorCancellation] CancellationToken ct)
{
    await foreach (var item in _stream.SubscribeWithHeartbeat(userId, TimeSpan.FromSeconds(20), ct))
        yield return item;   // "alert" events, plus a "ping" every 20s
}
```

Live only. No cursor, no header to read, no backfill query — roughly forty lines less than the replay design, and the endpoint fits on a screen.

⚠️ **The 20-second heartbeat is not optional.** Azure Container Apps' `requestIdleTimeout` is **4 minutes**, and 4 is both the default *and* the floor on Consumption — raising it requires a Dedicated D4+ workload profile with at least two nodes, costing more than the rest of the stack. `SseFormatter` has **no comment API**, so the heartbeat must be a real named event the client ignores: `new SseItem<AlertDto?>(null, eventType: "ping")`.

The same heartbeat keeps you above Kestrel's `MinResponseDataRate` (240 B/s), which applies to SSE — WebSockets are exempt, SSE is not.

### 2.6 The ticket handshake

`EventSource` cannot set headers — the constructor takes only `(url, { withCredentials })`. With the SPA on GitHub Pages and the API on Container Apps the origins differ, and cross-origin cookies are unreliable now that third-party cookies are being phased out. So:

```
POST /api/alerts/stream-ticket     [Authorize]  → { ticket, expiresIn: 30 }
GET  /api/alerts/stream?ticket=…   validated by the ticket
```

The ticket is 32 random bytes in Redis with a 30-second TTL, **deleted on first use**. A long-lived JWT in a query string lands in access logs, browser history and proxy logs; a single-use 30-second ticket does not meaningfully. Set `Microsoft.AspNetCore.Hosting` logging to `Warning` anyway.

### 2.7 Manual trigger — the brief asks for it explicitly

```
POST /api/alerts/simulate    [Authorize]    202
```

Picks one of the caller's held tickers, synthesises a `FiredAlert` with a plausible delta, and persists and publishes it through the **real** path — not a fake push straight to the socket. Marked `isSimulated: true` and rendered with a badge.

This is what makes the feature demonstrable outside market hours, when nothing streams and no threshold will breach on its own. Phase 3's fake provider also exposes `POST /api/dev/nudge?ticker=AAPL&pct=-7`, which drives a *genuine* evaluation-triggered alert — worth showing in the README, because it demonstrates the real path rather than the simulate shortcut.

### 2.8 Endpoints

```
GET   /api/alerts/settings          200 + AlertSettingsDto           [Authorize]
PUT   /api/alerts/settings          200 | 400                        [Authorize]
GET   /api/alerts?limit=50          200 + FiredAlertDto[]            [Authorize]
POST  /api/alerts/stream-ticket     200 + { ticket }                 [Authorize]
GET   /api/alerts/stream?ticket=    200 text/event-stream
POST  /api/alerts/simulate          202                              [Authorize]
```

---

## 3. Frontend

### The stream hook

`src/features/alerts/useAlertStream.ts` — mounted **once** inside `_authenticated`, not per component.

```ts
useEffect(() => {
  let es: EventSource | null = null
  let retryTimer: ReturnType<typeof setTimeout> | null = null
  let cancelled = false

  async function connect() {
    if (cancelled) return
    const { ticket } = await fetchTicket()
    es = new EventSource(`${API}/api/alerts/stream?ticket=${ticket}`)
    es.addEventListener('alert', (e) => {
      const alert = JSON.parse((e as MessageEvent).data)
      queryClient.setQueryData(alertKeys.list(), (old = []) => [alert, ...old])
    })
    es.addEventListener('ping', () => setConnected(true))
    es.onerror = () => {
      es?.close()
      setConnected(false)
      queryClient.invalidateQueries({ queryKey: alertKeys.list() })   // refetch history on reconnect
      retryTimer = setTimeout(connect, backoff())
    }
  }
  connect()

  return () => { cancelled = true; es?.close(); if (retryTimer) clearTimeout(retryTimer) }
}, [])
```

That `invalidateQueries` on error is the whole replacement for cursor replay. Anything fired while disconnected comes back on the next history fetch, using machinery TanStack Query already provides.

⚠️ **React 19 StrictMode double-invokes effects.** Without `cancelled` and the `clearTimeout` in cleanup you get two live connections, and an SSE stream permanently occupies one of the browser's **6 connections per origin** on HTTP/1.1.

⚠️ `EventSource` reconnects automatically, but the ticket in the URL is already spent, so the server would reject it. That is why `onerror` closes and reconnects manually with a fresh ticket. Note the trade-off in the README: you give up the browser's free reconnect in exchange for header-less auth. About 15 lines.

### Screens

The alerts panel on the dashboard is the mockup's right-hand column — threshold pill (`±2%`), rows of `(ticker, delta, time, text)`, and an empty state. It loads history with `useQuery` on mount and receives new alerts by push. `/notifications` is the mockup's fifth screen, showing the same data with a higher limit.

The live badge in the app shell reads **"Live (SSE)"**, not "WS Live". The brief's §5 grades consistency, and shipping SSE under a WebSocket label is a self-inflicted wound.

---

## 4. Infrastructure delta

```bicep
scale: {
  minReplicas: 1
  maxReplicas: 2
  rules: [ { name: 'http', http: { metadata: { concurrentRequests: '100' } } } ]
}
```

⚠️ The default `concurrentRequests` is **10**. A held-open SSE stream may count as one in-flight request for its entire life, so 30 connected browsers would scale to 3 replicas — scaling on *user count* rather than load. Raise it to 100 and cap `maxReplicas: 2`, which is also what the Postgres connection budget demands.

⚠️ A replica with an open SSE connection **never qualifies for ACA's reduced idle billing rate**, which requires no in-flight HTTP requests. Budget at the active rate; the overview's cost table already does.

Do **not** add `UseResponseCompression()` anywhere — it wraps the body in a buffering stream and the feed dies silently. ACA's Envoy does not buffer `text/event-stream` and exposes no buffering knob, so there is nothing to configure there; `X-Accel-Buffering` is an nginx convention, sent anyway for the compose path. `terminationGracePeriodSeconds` defaults to 480, plenty for streams to close cleanly during a revision swap.

In compose, the nginx SSE location block from Phase 1 finally gets exercised. Verify `proxy_buffering off` is actually in effect — without it, events batch and nothing arrives until the response ends, which never happens.

---

## 5. Tests

### Unit — `Portfolio.UnitTests`

| Test | Asserts |
|---|---|
| `Evaluate_DrawdownBeyondThreshold_Fires` | Baseline |
| `Evaluate_RunUpBeyondThreshold_Fires` | |
| `Evaluate_MoveBelowThreshold_DoesNotFire` | |
| `Evaluate_PartialReversal_150to141to149_DoesNotFireRunUp` | **The false-positive fix**, encoding whichever constraint you chose |
| `Evaluate_OscillatingWithinBand_FiresOnceNotEveryCycle` | The systematic version of the same bug |
| `Evaluate_TooFewSamples_DoesNotFire` | Guard 1 |
| `Evaluate_WindowSpansSessionBoundary_DoesNotFire` | Guard 2 |
| `Evaluate_StaleFeed_SuppressesPriceAlert_RaisesFeedHealth` | Guard 3 |
| `Cooldown_SecondBreachWithinTtl_Suppressed` | |
| `Cooldown_AfterTtlExpiry_Fires` | |
| `Cooldown_IsPerTickerAndDirection` | A drawdown cooldown does not suppress a run-up |
| `RemoveHolding_ClearsCooldown_BothDirections` | Was `HoldingRemoved_ClearsCooldown`; now an in-module call, not an event handler |
| `Settings_WindowAboveOneHour_Rejected` | |
| `Settings_WindowExceedingRetention_Rejected` | |

### Integration — `Api.IntegrationTests`

Read the stream with the BCL `SseParser.Create<T>` over `HttpCompletionOption.ResponseHeadersRead`.

| Test | Asserts |
|---|---|
| `Simulate_PersistsAlert_AndPushesToOpenStream` | End to end, under a second |
| `Simulate_WithNoStreamOpen_StillPersists` | Then appears in the history endpoint |
| `Stream_WithoutTicket_Returns401` | |
| `Stream_WithExpiredTicket_Returns401` | |
| `Stream_TicketIsSingleUse_SecondAttemptFails` | |
| `Stream_EmitsHeartbeat_WithinTwentyFiveSeconds` | **The ACA 4-minute-timeout test** |
| `Stream_AlertPublishedOnAnotherConnection_IsReceived` | Redis pub/sub fan-out |
| `Stream_ClientDisconnects_ServerReleasesSubscription` | No leak |
| `History_ReturnsMostRecentFirst_RespectsLimit` | |
| `History_OnlyReturnsCallersAlerts` | |
| `Evaluation_AfterNudge_FiresRealAlert` | The *real* path, not simulate |
| `Alert_PersistedBeforePublish_SurvivesPublishFailure` | Kill Redis; the row is still in Postgres and the history endpoint returns it |

### Frontend

`connects once under StrictMode` (count `EventSource` constructions) · `pushes alert into the query cache` · `reconnects with a fresh ticket after an error` · `invalidates history on reconnect` · `renders "Live (SSE)" not "WS Live"`

---

## 6. Gotchas

**`SseFormatter` has no comment API.** The idiomatic `:heartbeat\n\n` is unreachable through the typed API, so use a named event the client ignores.

**Response compression buffers SSE to death.** `text/event-stream` is not in the default MIME list, but adding custom MIME types or `EnableForHttps = true` pulls it in. Simplest safe answer: do not add the middleware.

**nginx breaks SSE three independent ways by default** — `proxy_buffering on`, `proxy_read_timeout 60s` (stricter than ACA's 240s), and HTTP/1.0 upstream on older builds. All three are handled in the Phase 1 config block; verify it applies to this location.

**HTTP/1.1's 6-connections-per-origin cap.** An SSE stream never completes, so it holds a slot permanently. Marked "won't fix" in Chrome and Firefox. Fine here — one stream plus five concurrent fetches — but document it, and never open the stream more than once.

**`decimal` in the alert payload** has the same JSON-number problem as Phase 3. Serialise money as strings.

**Redis pub/sub is fire-and-forget.** A subscriber that is down misses the message permanently. Acceptable only because the alert is already in Postgres and the panel refetches history on reconnect — which is exactly why persist-before-publish is not negotiable.

**Redis holds the cooldown, so a Redis outage means every breach fires.** Phase 6 suppresses alerts entirely when the cache is unavailable, which covers this. Worth knowing now so it isn't a surprise then.

---

## 7. Your call

### The false-positive constraint — `Portfolio.Domain/Alerts/ThresholdRule.cs`

```csharp
internal static class ThresholdRule
{
    /// <param name="window">Ordered observations, oldest first.</param>
    public static OneOf<Fires, DoesNotFire> Evaluate(
        IReadOnlyList<PriceObservation> window, decimal thresholdPercent)
    {
        // current / min / max are computed for you above.
        //
        // TODO(you): min/max-over-window catches a real move the endpoints miss —
        // Initial.md:128 is right about that. But as written it also fires
        // "+5.7% run-up" on a position that closed the hour DOWN 0.7%, and any
        // ticker oscillating in a band wider than the threshold fires forever.
        //
        //   (a) RECENCY — extreme must be within the last N samples.
        //       Catches "fell hard just now". Misses a slow grind from an old extreme.
        //
        //   (b) CURRENT IS THE EXTREME — only fire at new window highs/lows.
        //       Very quiet, very defensible, and gives up the exact partial-reversal
        //       case Initial.md added min/max for.
        //
        //   (c) SIGN AGREEMENT — endpoint delta and extreme delta must agree.
        //       Kills the oscillation loop, keeps genuine sharp moves, and is
        //       closest to what «% за проміжок часу» in req 9 literally says.
        //
        // Put BOTH deltas in the payload and name the comparison in the alert text.
        // This is the decision an interviewer will push hardest on — the reasoning
        // matters more than the choice.
    }
}
```

About ten lines. Write it before `Evaluate_PartialReversal_150to141to149_DoesNotFireRunUp`, since that test *is* the specification.

---

## 8. Done when

- [ ] `docker compose up`, log in, set threshold to 1%
- [ ] Click **Simulate** → alert appears in the panel in under a second, with the simulated badge
- [ ] Reload the page → the alert is still listed (history fetch, not replay)
- [ ] Simulate with the tab closed, then open the app → the alert is in the list
- [ ] Leave the tab open for **5 minutes** → the connection is still alive (proves the heartbeat)
- [ ] `POST /api/dev/nudge?ticker=AAPL&pct=-7` → a **real** evaluation-driven alert fires, no simulate involved
- [ ] Nudge twice inside the cooldown → only one alert
- [ ] Nudge ±3% repeatedly with a 2% threshold → alerts stay bounded, not one per cycle
- [ ] Delete the holding → its cooldown is cleared (`redis-cli KEYS 'alerts:cooldown:*'`)
- [ ] `/notifications` lists history; the shell badge reads **"Live (SSE)"**
- [ ] `dotnet test` green, including `Stream_EmitsHeartbeat_WithinTwentyFiveSeconds`
- [ ] `npm test` green, including `connects once under StrictMode`
- [ ] Deployed: alerts arrive on the GitHub Pages URL from the Azure API, and the stream survives past 4 minutes
- [ ] `docs/Initial.md:22` corrected — thresholds are owned by the alerts feature in Portfolio, not by Identity
- [ ] `alert_settings` and `fired_alerts` are in the **`portfolio`** schema, reached by `portfolio_svc`, with no `alerts` schema in the migration
- [ ] README: the SSE vs WebSocket decision matrix · the ticket handshake and why · the heartbeat and the ACA 4-minute floor · why replay was dropped · the false-positive constraint you chose
- [ ] Alerts panel usable at 375px
