# Phase 6 — Doesn't break · 0.75 days

## 1. Goal

Kill the quote provider → the app keeps working, shows the last price it saw with an honest age, and the health panel goes amber. Kill Redis → fresh prices still render, because Redis is only the fallback; alerts are suppressed and say so. Kill Postgres → a clear error, not a white screen.

> **Corrected.** This line used to read *"kill Redis → prices still render from a last-known-good cache"*, which was circular — the last-known-good cache **was** Redis. With the dashboard asking the provider directly (Phase 3 §2.5), losing Redis costs the fallback and nothing else.

Covers P1 req 10 end to end, and the brief's grading criterion 3 — *«коректна обробка помилок і edge-кейсів»*.

This is also the **buffer phase**. If an earlier phase overran, this is where the time comes from. Cut in this order: BYOK polish → the Postgres-down path → the responsive re-pass. Never cut the provider-down path, which is the one a reviewer will actually trigger by leaving a bad API key in `.env`.

---

## 2. Backend

### 2.1 A health contract worth rendering

`GET /api/health/detail` — authorised, distinct from the unauthenticated `/health` that ACA probes.

```csharp
public sealed record HealthDetailDto(
    ComponentHealth Database,     // Healthy | Degraded | Unhealthy
    ComponentHealth Cache,
    ComponentHealth QuoteFeed,
    DateTimeOffset? LastSuccessfulPoll,
    DateTimeOffset? OldestObservation,
    int TickersInPollSet,
    string ProviderName,          // "Finnhub" | "Fake"
    int? RateLimitRemaining);
```

**Three states, not two.** `Degraded` is the whole point: the quote feed is `Degraded` when the last successful poll is older than two cycles but observations still exist, and `Unhealthy` only when there is nothing left to serve. A binary up/down hides exactly the state this phase exists to make visible.

`ProviderName` on the panel is deliberate — a reviewer running without an API key should be able to *see* that the fake provider is active rather than wonder why prices look synthetic.

### 2.2 Feed-health signal

Phase 4 raises a feed-health signal when a stale feed suppresses price alerts. It gets a home here: a `FeedHealthState` singleton, updated by the poller each cycle, read by the health endpoint and by the alert evaluator. The evaluator lives in Portfolio (alerts is a feature area there, not a module), so the singleton is registered by the host and read from both sides.

This closes the loop on `Initial.md:130` — *«no new data must never read as "nothing moved"»*. Until now that rule existed only inside the evaluator. Now the user can see it.

### 2.3 Provider failure

The resilience pipeline from Phase 3 handles retry, circuit breaker and timeout. What this phase adds is what happens **after** the pipeline gives up:

| Failure | Behaviour |
|---|---|
| **429 with `Retry-After`** | Honoured (default — do **not** assign a `DelayGenerator`, it silently disables this). Cycle skips the remaining symbols, logs one warning, `RateLimitRemaining` drops to 0. |
| **Circuit open** | Poll cycles return immediately without calling the provider. Read-through returns cached values only. Feed → `Degraded`. |
| **Timeout / 5xx** | Retried, then the cycle ends. Observations already written stay. |
| **Malformed response** | Logged with the raw body at Debug, symbol skipped, other symbols unaffected. One bad ticker must not kill a cycle. |

The critical property: **a provider outage degrades the dashboard's numbers, it never fails the request**. `Initial.md:148` is right that the freshness timestamp does the honest work — this phase makes sure the timestamp is actually reached rather than replaced by a 500.

### 2.4 Redis failure

`Initial.md:96` specifies a last-known-good in-memory cache per replica. **Do not build it.** Its failure mode is silent inconsistency — at two replicas two users see different prices for the same ticker, and every restart empties it. The `marketdata:last:{ticker}` key from Phase 3 §2.4 does the same job, shared across replicas and surviving restarts, and it is already written by the path that fetches. A third tier under it would be a cache for a cache.

Scope it accordingly — **latest price per ticker only**, not the window. The window is what alerts need, and serving a fabricated window would fire fabricated alerts. So:

- Redis down, provider up → dashboard renders **fresh** prices; only the degradation fallback is lost, and nothing visible changes
- Redis down → **alerts are suppressed entirely**, feed → `Degraded`, and the alerts panel says so rather than sitting silently empty

That asymmetry is the correct one and worth a README sentence: a stale price is a degraded read; a fabricated window is a wrong alert.

`abortConnect=false` on the connection string so the multiplexer reconnects rather than staying permanently poisoned after one startup blip.

### 2.5 Postgres failure

Nothing clever. Health reports `Unhealthy`, the API returns 503 with `ProblemDetails`, and the SPA shows a full-page retry state rather than a white screen. EF's `EnableRetryOnFailure` handles transient blips; a genuinely down database is not something to paper over.

⚠️ If `EnableRetryOnFailure` is on, any explicit transaction **must** be wrapped in `Database.CreateExecutionStrategy().ExecuteAsync(...)`, and the enclosed work must be safe to re-run. This is where the Phase 1 transaction decorator gets its test.

### 2.6 Startup validation

Fail fast, with a message that says what to fix:

- Retention > max configurable alert window (from Phase 3)
- Every required connection string present and parseable
- JWT signing key present and at least 32 bytes
- If `Finnhub__ApiKey` is set, one validation call at startup — log a warning and fall back to the fake provider if it fails, rather than discovering it on the first poll

A container that refuses to start with a clear reason beats one that starts and serves nothing.

---

## 3. Frontend

### API health panel

The mockup's panel, wired for real: one row per component with a coloured dot, the provider name, last successful poll as relative time, and tickers tracked. Polls `/api/health/detail` every 30s — independent of the dashboard query, so a dashboard failure doesn't take the health panel down with it.

### Query-level error handling

```ts
new QueryClient({
  defaultOptions: {
    queries: {
      retry: (failureCount, error) =>
        error instanceof ApiError && error.status >= 400 && error.status < 500
          ? false                        // never retry a 4xx
          : failureCount < 3,
      throwOnError: false,               // handle inline, don't unmount the route
    },
  },
  queryCache: new QueryCache({ onError: (e, query) => toastOnce(e, query) }),
})
```

**Never retry a 4xx.** Retrying a 401 three times before refreshing wastes the refresh window; retrying a 400 is pointless. Only 5xx and network errors retry.

`throwOnError: false` on the dashboard query is the reason Phase 3 avoided a route loader: keep the last good table on screen with an inline banner, rather than replacing the whole route with an error component.

### The three visible states

| State | UI |
|---|---|
| **Fresh** | Normal. "Updated 12s ago". |
| **Stale** | Amber banner: "Prices last updated 6 minutes ago — the quote provider is not responding." Table still shows the last known numbers, dimmed. |
| **Unavailable** | Table shows structure with "—" in price columns, totals show cost only, explicit "prices unavailable" note. **Never $0.00.** |

### Error boundaries

One per route under `_authenticated`, plus a root boundary. A crash in the alerts panel must not take the dashboard down. Each boundary offers Retry, which resets the query rather than reloading the page.

### Mutation failures

Add/edit/delete holdings roll back their optimistic update and show the server's `ProblemDetails` message inline on the form — not a toast that disappears before it is read.

⚠️ This is where Phase 2's TanStack Query v5.89 signature change bites hardest: a wrong `onError` signature rolls back the wrong snapshot, and it only shows up when a mutation actually fails. The Phase 2 test covers it; re-verify here with the provider genuinely down.

### Offline

`navigator.onLine` plus the SSE connection state drive a thin "reconnecting" bar. On reconnect, Phase 4's stream hook invalidates the alerts query, so anything fired while disconnected arrives on the next history fetch — using machinery TanStack Query already provides rather than a replay protocol.

---

## 4. Infrastructure delta

**Bicep**

```bicep
probes: [
  { type: 'Startup',   httpGet: { path: '/health/startup',  port: 8080 }, failureThreshold: 30, periodSeconds: 2 }
  { type: 'Liveness',  httpGet: { path: '/health/live',     port: 8080 }, periodSeconds: 30 }
  { type: 'Readiness', httpGet: { path: '/health/ready',    port: 8080 }, periodSeconds: 10 }
]
```

Three endpoints with genuinely different meanings, because ACA acts on them differently:

- `/health/live` — **process is alive**. Must not check Postgres or Redis. A dependency outage that fails liveness gets your container killed and restarted in a loop, turning a degraded app into a down one.
- `/health/ready` — **can serve traffic**: Postgres reachable. Fails → removed from the load balancer, not killed.
- `/health/startup` — migrations applied, config validated. Generous `failureThreshold` so a cold start is not mistaken for a crash.

Getting liveness wrong is the single most common way a Container Apps deployment turns a partial outage into a total one.

**Compose** — same split, plus `restart: unless-stopped` on the API.

**Workflow** — a post-deploy smoke step: `/health/detail` returns 200 with `Database: Healthy`, then open the SSE stream and assert a `ping` inside 30s. Fail the deploy if not.

---

## 5. Tests

### Unit

| Test | Asserts |
|---|---|
| `FeedHealth_NoPollForTwoCycles_ReportsDegraded` | |
| `FeedHealth_NoObservationsAtAll_ReportsUnhealthy` | The three-state distinction |
| `FeedHealth_RecoversAfterSuccessfulPoll` | |
| `LastKnownGood_ServesLatestPrice_WhenCacheUnavailable` | |
| `LastKnownGood_DoesNotServeWindow` | Fabricated windows would fire fabricated alerts |
| `Startup_MissingJwtKey_ThrowsWithClearMessage` | |
| `Startup_ShortJwtKey_Throws` | |
| `Startup_InvalidProviderKey_FallsBackToFake_WithWarning` | Doesn't refuse to start |

### Integration — `Api.IntegrationTests`

| Test | Asserts |
|---|---|
| `Dashboard_ProviderThrows_Returns200WithStaleFlag` | Not 500 |
| `Dashboard_Provider429_Returns200_HonoursRetryAfter` | And `RateLimitRemaining` is 0 |
| `Dashboard_CircuitOpen_ServesCachedWithoutCallingProvider` | Call counter stays flat |
| `Dashboard_RedisStopped_ServesLastKnownGood` | **Stop the Testcontainer mid-test** |
| `Alerts_RedisStopped_AreSuppressed_NotFabricated` | The asymmetry, enforced |
| `Health_ProviderDown_ReportsQuoteFeedDegraded` | |
| `Health_Live_DoesNotTouchDatabase` | The liveness rule — assert with a stopped Postgres container |
| `Health_Ready_FailsWhenDatabaseDown` | |
| `OneBadTicker_DoesNotAbortPollCycle` | Malformed response for one symbol; the others still write observations |
| `Transaction_UnderRetryStrategy_IsWrappedInExecutionStrategy` | The Phase 1 decorator, finally exercised |

### Frontend

`stale response renders amber banner and keeps the table` · `unavailable price renders "—" not $0.00` · `4xx is not retried` (MSW request counter) · `alerts panel crash does not unmount the dashboard` · `mutation failure restores the correct pre-mutation snapshot` · `offline bar appears and clears on reconnect`

### Manual chaos

Scripted in the README so a reviewer can reproduce:

```bash
docker compose stop redis      # dashboard degrades, alerts suppressed
docker compose start redis     # recovers within one cycle
docker compose stop postgres   # 503 + retry screen, container NOT restart-looping
```

---

## 6. Gotchas

**Liveness must not check dependencies.** A liveness probe that pings Postgres turns a database blip into a container restart loop, and ACA will keep restarting. This is the highest-consequence mistake in the phase.

**`Retry-After` is honoured by default and silently disabled by `DelayGenerator`.** Repeating it from Phase 3 because this is the phase where you will be tempted to tune the retry strategy.

**A circuit breaker needs a minimum throughput before it opens.** With one call per ticker per minute, the default sampling window may never accumulate enough calls to trip. Configure `MinimumThroughput` and `SamplingDuration` against your actual cadence, or the breaker is decoration.

**`abortConnect=false` on Redis.** Without it, a multiplexer that fails its first connect stays permanently broken even after Redis returns — the app looks like it never recovers.

**`EnableRetryOnFailure` + explicit transactions throws** *"The configured execution strategy does not support user initiated transactions"* unless wrapped in `CreateExecutionStrategy().ExecuteAsync(...)`. And the wrapped delegate re-runs wholesale on a transient failure, so everything inside must be idempotent.

**`ProblemDetails` needs `AddProblemDetails()` registered** or unhandled exceptions return an empty body with a status code, and the SPA has nothing to display.

**Do not log the raw provider response at Information.** It contains the API key in the request URL if you fell back to `?token=`. Debug level, and prefer the header form from Phase 3.

**Toast-only errors on mutations get missed.** The brief grades error handling; an error the user has to catch within three seconds does not count as handled.

---

## 7. Your call

### Degradation thresholds — `MarketData.Application/FeedHealthPolicy.cs`

```csharp
internal static class FeedHealthPolicy
{
    // TODO(you): when does the UI stop trusting a price?
    //
    //   STALE THRESHOLD — after how long without a successful poll does the banner
    //     appear? Too tight and a single slow cycle cries wolf on every deploy;
    //     too loose and the reviewer sees confidently-presented 20-minute-old prices.
    //     Note this interacts with the user's refresh interval from Phase 5: someone
    //     polling at 15s sees "stale" four times sooner than someone at 60s unless
    //     you anchor it to the SERVER cadence.
    //
    //   UNAVAILABLE THRESHOLD — when do you stop showing numbers at all and switch
    //     to "—"? Showing an hour-old price with a small amber note is arguably
    //     worse than showing nothing, because the number still anchors the reader.
    //
    //   WEEKEND — Friday's close on a Sunday is stale by every clock-based measure
    //     and yet is the correct, only, and freshest possible answer. A pure
    //     time-since-poll rule marks a perfectly healthy weekend app as degraded.
    //
    // This is the phase's whole point: honest degradation. The numbers you pick
    // here are what a reviewer actually sees when they inevitably run it with a
    // bad API key.
    public static FeedState Classify(DateTimeOffset? lastPoll, DateTimeOffset? newestObservation) => …;
}
```

~10 lines. Write it before `FeedHealth_NoPollForTwoCycles_ReportsDegraded`, since the test encodes the numbers.

---

## 8. Done when

- [ ] `docker compose up`, then `docker compose stop redis` → dashboard still renders prices from last-known-good, marked stale; alerts panel says alerts are suppressed
- [ ] `docker compose start redis` → recovers within one cycle, banner clears
- [ ] Set an invalid `Finnhub__ApiKey` and restart → app **starts**, logs a warning, falls back to the fake provider, health panel shows "Fake"
- [ ] Block the provider mid-session → amber banner within the stale threshold, table keeps the last good numbers, no 500 anywhere in devtools
- [ ] `docker compose stop postgres` → 503 with a readable message and a Retry button, and `docker ps` shows the API **not** restart-looping
- [ ] Force an error inside the alerts panel → the dashboard stays up
- [ ] Fail a holding mutation → the row reverts to its correct previous value, message shown inline on the form
- [ ] Go offline → reconnecting bar; back online → SSE replays the gap with no manual refresh
- [ ] `dotnet test` green, including `Health_Live_DoesNotTouchDatabase` and `Alerts_RedisStopped_AreSuppressed_NotFabricated`
- [ ] `npm test` green
- [ ] Deployed; repeat the provider-down case against the Azure API
- [ ] **README complete** — the brief asks for a *короткий* description, so ~1 page plus a link to `docs/Initial.md`:
  - [ ] `docker compose up` instructions, working from a clean clone with no API key
  - [ ] The **SSE vs WebSocket decision matrix**
  - [ ] **Parameterisation evidence** for P0 req 6 — state that the project uses EF Core throughout with no hand-written SQL, because the brief permits "raw or query builder" and asks only for parameterisation, which EF Core makes structural rather than conventional. Then show it rather than claiming it: paste one generated statement with its `@p0` placeholders beside the `DbParameter` values, and name the three tests in `Api.IntegrationTests` that enforce it — including the fixture-wide interceptor asserting no user-supplied value ever appears in `CommandText`
  - [ ] The fake provider, and why it is the default
  - [ ] The BYOK design
  - [ ] The Azure deployment, cost, and `az group delete` teardown
  - [ ] A trimmed "what we rejected, and why" table from `Initial.md:156-172` — the single best evidence for the brief's grading criterion 4
  - [ ] Known limits: the recomputed ticker ceiling, the per-replica last-known-good cache's inconsistency, HTTP/1.1's 6-connection cap
- [ ] `docs/Initial.md` corrections applied — alert-settings ownership, the window-claim overlap guard, the alert example's arithmetic. Its four-module description is **not** corrected: the file is historical, and `00-overview.md` §"Three modules, not four" is where the current shape and its reasoning live
- [ ] The `alerts` schema, the `alerts_svc` role and the `ALERTS_PW` / Alerts-connection-string variables are gone from `db/init/`, `docker-compose.yml`, `infra/` and the workflows — **verified by a clean-clone `docker compose up`**, which is the only thing that can prove it (see `docs/deferred-work.md`)
- [ ] Full verification walkthrough from `00-overview.md` §Verification passes end to end, locally and deployed
