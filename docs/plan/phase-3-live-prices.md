# Phase 3 — Live prices & P&L · 0.8 days

## 1. Goal

The dashboard shows real prices, per-position and total value / cost / profit in currency **and** percent, position weight, and honest freshness timestamps. A ticker added seconds ago is priced on its first render, because the request fetches it. When the provider is unreachable the table still shows the last price it saw, with its age.

**No background service ships in this phase.** The poller and the price window are alert infrastructure and arrive in Phase 4; see §2.6.

Covers P0 reqs 2, 5 and 6, plus the backend half of req 10.

---

## 2. Backend

### 2.1 Provider abstraction — `MarketData.Domain`

```csharp
public interface IQuoteProvider
{
    /// Takes a SET of symbols. Each quote carries its OWN observation timestamp.
    Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlySet<Ticker> symbols, CancellationToken ct);
    Task<bool> SymbolExistsAsync(Ticker symbol, CancellationToken ct);
}

public readonly record struct Quote(Ticker Ticker, decimal Price, DateTimeOffset ObservedAt);
```

The per-quote timestamp looks redundant under polling — every quote in a cycle is equally fresh. It exists for the WebSocket-ingestion path in `Initial.md:171`, where one symbol may be seconds old and another twenty minutes. Code that assumes batch-level freshness breaks silently the day you switch.

### 2.2 `FinnhubQuoteProvider`

Verified against Finnhub's docs and swagger on 2026-08-01:

- **60 calls/minute** on the free tier, plus a **30 calls/second** global burst cap on all plans. Over-limit → HTTP 429.
- **No batch endpoint.** `symbol` is singular; there is no `symbols` parameter. One call per ticker.
- Auth: `X-Finnhub-Token` header. The `?token=` query param also works — use the header, it keeps the key out of proxy logs.
- `GET /api/v1/quote?symbol=AAPL` → `{ c, d, dp, h, l, o, pc, t }`

| Field | Meaning | Note |
|---|---|---|
| `c` | Current price | |
| `d` | Change, absolute | **nullable** |
| `dp` | Percent change | **nullable**, already a percent (`1.23` = 1.23%) |
| `pc` | Previous close | |
| `t` | Timestamp | **UNIX seconds** — the WebSocket feed uses milliseconds |

⚠️ Their docs are inconsistent: the response-attributes table lists seven fields and omits `t`; the sample shows `t` but omits `d`/`dp`. The live API returns all eight. Model `d` and `dp` as `decimal?`.

⚠️ **`dp` is the wrong number for your threshold.** It is change versus the *previous session close*, not versus your window. Compute the threshold from your own observations. Say so in the README — it is exactly what an interviewer probes.

⚠️ **An all-zero response (`c: 0, pc: 0`) means an unresolvable or unentitled symbol**, not "market closed". Outside market hours `/quote` returns the last trade values with a frozen `t`. Map all-zero to `UnknownTicker`, not to a $0 price.

Resilience via `Microsoft.Extensions.Http.Resilience` on the typed client: retry, circuit breaker, timeout.

⚠️ `Retry-After` on 429 is honoured **by default** — but **assigning a `DelayGenerator` silently disables it**. Do not set one. Better still, rate-limit client-side with `System.Threading.RateLimiting` so you never retry into a 429.

### 2.3 `FakeQuoteProvider` — mandatory, not optional

Finnhub shut its sandbox down in September 2022 and there is no demo key, so without this the grader must register for an API key before the app does anything.

Seeded random walk: deterministic per `(ticker, minute)` from a fixed seed, configurable volatility and drift, plus a `/api/dev/nudge?ticker=AAPL&pct=-7` hook Phase 4 uses to make an alert fire on demand.

**It is the default when no API key is configured.** Startup logs a single warning line naming which provider is active. Integration tests always use it — they never touch the network.

### 2.4 Recording what was fetched

Every quote this app pays an API call for gets written down, from whichever path fetched it:

```
marketdata:last:{ticker}  ->  "{price}:{epochMs}"     one value, overwritten, never trimmed
```

That is the whole mechanism. There is no poller in this phase, no sorted set, no window, no read-through
and no coalescer — all of that is alert infrastructure and lands in Phase 4, because only alert evaluation
needs price *history*.

The write is **best-effort**. If Redis is unreachable the fetch already succeeded and the caller must still
get their prices; a failed cache write may never fail the request.

### 2.5 When Finnhub is unreachable

The dashboard asks Finnhub first, always. Only when that fails does it read `marketdata:last:{ticker}`, and
it renders whatever it finds **with its age**:

| Situation | What the user sees |
|---|---|
| Finnhub answers | fresh prices, no age marker |
| Finnhub down, a last price exists | that price, in amber, "6 minutes ago" |
| Finnhub down, never fetched this ticker | the position with price and P&L blank, excluded from totals |
| Finnhub and Redis both down | as above — two simultaneous failures do not deserve a third mechanism |

Always show the age rather than hiding anything past some staleness threshold: a cap just recreates the
blank it was meant to avoid, and the reader can judge for themselves whether a six-minute-old price matters.

⚠️ **This is not read-through, and the difference is the order.** Read-through checks the cache first and
fetches on a miss. Here the request always goes to the provider and only falls back to Redis when it cannot.
The earlier design had it the other way round and grew a coalescer, an in-memory tier and a "make the
Saturday demo work" justification on top of it. Removing that is most of why this phase went from 1.1 days
to 0.8.

### 2.6 What moved to Phase 4, and why

Everything that samples prices over time:

| Was here | Now | Because |
|---|---|---|
| `QuotePollingService`, the 60s `PeriodicTimer` | Phase 4 | Only alert evaluation needs repeated samples |
| `marketdata:prices:{ticker}` sorted set, retention, trimming | Phase 4 | It is a *window*; the dashboard wants one number |
| `marketdata:claim:{window}`, `marketdata:cycle-inflight` | Phase 4 | Both are locks that only matter when something polls |
| Read-through, the fetch coalescer, the in-memory tier | deleted | They existed to make the dashboard's trip through alert infrastructure survivable |
| `minReplicas: 1` | Phase 4 | Scale-to-zero only breaks something once a background service exists |

The test that they are genuinely independent: with no alerts configured anywhere, nothing polls, every
sorted set is empty, **and this phase's dashboard behaves identically**.

⚠️ The poller in Phase 4 polls only tickers with an active alert, so it is *not* a superset of the
dashboard's fetches. The same ticker may be fetched twice within a minute by the two paths. That is a
handful of duplicate calls, and removing it would mean the dashboard consulting the alert window — which is
read-through again, wearing a hat.

### 2.7 Dashboard query

`GetDashboard(UserId)` → `DashboardDto`. Holdings from Postgres, prices via `IMarketDataQueries` (MarketData's contract), joined **in memory**. `AsNoTracking()`, projected straight to a DTO, never materialising the `Holding` aggregate.

The in-memory join is the visible consequence of prices not living in Postgres: you cannot sort or filter by current value in the database. At twenty holdings that costs nothing; it would matter at thousands.

**Per position:** quantity, average price, current price (nullable), market value, cost, profit in currency and percent, weight, `observedAt`.
**Totals:** value, cost, profit in currency and percent, `asOf`, `stalestObservedAt`.

All money computed server-side in `decimal`.

⚠️ **Serialise money as strings.** `System.Text.Json` writes `decimal` as a JSON number and `JSON.parse` materialises it as an IEEE-754 double — the precision you computed server-side is destroyed at the boundary regardless. `Initial.md:108` gets the instinct right and stops one step short.

⚠️ **Weight must be computed server-side too.** The mockup has the column; computing `value / total` in the browser reintroduces exactly the float arithmetic the `decimal` rule exists to prevent.

A ticker with no price returns `null` and renders as **pending**, never `$0.00` — a zero would flow into the totals as a complete loss on that position.

**No raw SQL anywhere. EF Core only.** The brief says *«SQL-запити — сирі або через query builder, головна вимога — параметризація»* — raw or query builder, the requirement is parameterisation. EF Core makes parameterisation **structural** rather than merely conventional: there is no API surface that concatenates a value into command text, so the failure mode the brief is guarding against cannot be written.

The dashboard's own read is plain LINQ, and it is the only Portfolio query this phase adds:

```csharp
public Task<List<HoldingRow>> GetVisibleHoldingsAsync(Guid userId, CancellationToken ct) =>
    db.Holdings.AsNoTracking()
      .Where(h => h.UserId == userId && h.IsVisible)
      .Select(h => new HoldingRow(h.Ticker.Value, h.Quantity, h.AveragePrice))
      .ToListAsync(ct);
```

⚠️ The two cross-module reads this section used to show — every ticker anyone holds, and who holds a given
ticker — are **not here any more**. The first belongs to the poller and the second to alert evaluation, both
of which moved to Phase 4, and both are sourced from Alerts rather than Portfolio now that alerts carry
their own ticker subscriptions. See [module-boundaries.md](module-boundaries.md).

Proving it is the job of a test and a README line rather than of hand-written SQL — see §5 and Phase 6's README checklist.

---

## 3. Frontend

### Route

`src/routes/_authenticated/dashboard.tsx` — mockup's Dashboard, minus hero and ticker strip.

- **KPI tiles**: Total value, Total cost, Total P/L (currency + percent, coloured), Positions count
- **Holdings table**: Asset · Qty · Buy · Price · Value · P/L · P/L % · Weight
- **Freshness line**: "Updated 12s ago" from the headline `asOf`, plus a per-row timestamp where a position is materially staler than the rest — a thinly traded ticker can be minutes behind and a single global figure hides that
- **Refresh-interval control** — reads from settings, defaults to 60s. Phase 5 makes it persistent; this phase wires it to local state so the mechanism is proven
- **API-health panel** — stubbed here, filled in Phase 6

### Data — deliberately not a loader

```ts
const dashboardQuery = () => queryOptions({
  queryKey: dashboardKeys.view(),
  queryFn: fetchDashboard,
  refetchInterval: intervalMs,          // runtime-configurable
  refetchOnWindowFocus: true,
  staleTime: 0,
})
```

Phase 2 used a route `loader` for holdings. Quotes must **not**: a loader failure takes the whole route down with an error component, and the brief grades *visible degraded state*, not a blank screen. Plain `useQuery` keeps the last good data on screen while showing the error inline.

`refetchOnWindowFocus` matters — someone returning to the tab gets current data immediately instead of waiting out the cycle.

`refetchInterval` here is a user preference, not a mirror of anything server-side — there is no cycle to stay in step with, because each request fetches for itself. Push is reserved for Phase 4's alerts, where a breach can happen at any second and the client cannot know to ask.

### Money in the UI

The API returns money as strings. Parse with `Intl.NumberFormat` for display; never do arithmetic client-side. Percent values come pre-computed.

### Pending and stale states

A `null` price renders as a muted "—" with a "pending" tooltip and is excluded from the totals with a footnote. A stale headline timestamp turns the freshness line amber.

---

## 4. Infrastructure delta

Small, but real.

**Bicep**
- Add the AMR connection string as an ACA secret, referenced by `ConnectionStrings__Redis`
- Add `Finnhub__ApiKey` as a secret (empty in the default parameter file → the fake provider activates)
- **Set `FINNHUB_API_KEY` as a real GitHub secret.** Empty means the public URL serves invented prices for real tickers, which reads as broken rather than as a fallback. The fake provider is for the clean-clone path and the tests
- `minReplicas` stays **0**. Nothing runs in the background in this phase, so scale-to-zero costs nothing; Phase 4 raises it when the poller ships
- No `MarketData__PollIntervalSeconds` or `__RetentionMinutes` yet — there is nothing to configure until Phase 4

**Compose** — same env vars, pointing at the local Redis.

**Workflow** — no change.

⚠️ AMR is **TLS-only**. The connection string needs `ssl=true` and port `10000`, not `6379`. Local Redis in compose is plaintext on 6379, so the two connection strings differ in more than the host — parameterise the whole string, not just the hostname.

---

## 5. Tests

### Unit — `MarketData.UnitTests`

| Test | Asserts |
|---|---|
| `FinnhubResponse_NullDp_Deserialises` | Nullable `d`/`dp` |
| `FinnhubResponse_AllZero_MapsToUnknownTicker` | Not a $0 price |
| `FinnhubTimestamp_ParsedAsSeconds` | Not milliseconds |
| `FakeProvider_SameTickerSameMinute_SamePrice` | Determinism |
| `Fetch_WritesLastKnownPrice` | Every path that fetches records what it saw |
| `Fetch_RedisUnreachable_StillReturnsThePrice` | The cache write is best-effort and must never fail the request |
| `LastKnown_IsWorthShowing_EncodesTheStalenessCall` | §7's decision, whichever way it went |

The poller, window-claim, retention and coalescer tests are **gone from this phase** — they test things that
now ship in Phase 4, and two of them (`ReadThrough_*`) test a mechanism that no longer exists at all.

### Unit — `Portfolio.UnitTests` (P&L)

| Test | Asserts |
|---|---|
| `Position_ProfitInCurrencyAndPercent` | 20 @ $125 now $150 → +$500, +20% |
| `Position_Loss_IsNegativeNotAbsolute` | |
| `Totals_SumAcrossPositions` | |
| `Totals_ExcludeNullPricePosition` | And flag it, rather than treating it as $0 |
| `Weight_SumsToOneHundredPercent` | Within rounding tolerance |
| `Weight_WithNullPricePosition_ExcludesFromDenominator` | |
| `Money_SerialisedAsString_NotNumber` | The JSON precision trap |

### Integration — `Api.IntegrationTests`

Testcontainers Postgres + Redis, `FakeQuoteProvider`.

| Test | Asserts |
|---|---|
| `Dashboard_WithHoldingsAndPrices_ReturnsJoinedTotals` | The happy path |
| `Dashboard_NewlyAddedTicker_HasPriceOnFirstRequest` | The request fetches for itself; no pending state |
| `Dashboard_ProviderDown_ShowsLastKnownWithAge` | **The degradation test.** Kill the provider, assert the price and its `observedAt` still come back |
| `Dashboard_ProviderDown_NeverFetchedTicker_ReturnsNullNotZero` | A blank position, not a total loss |
| `Dashboard_ProviderReturns429_Returns200NotError` | Degraded, not broken |
| `Dashboard_RedisDown_StillReturnsFreshPrices` | The fallback store failing must not break the primary path |
| `Dashboard_OnlyReturnsCallersHoldings` | |
| `Dashboard_GeneratedSql_UsesParameterPlaceholder` | Capture the command text; it contains `@__userId_0` and **not** the literal id |

### Frontend

`renders totals from the API without client-side arithmetic` · `null price renders as pending, not $0.00` · `stale timestamp shows the amber freshness state` · `changing the interval control changes refetchInterval` · `provider error keeps the last good table on screen`

---

## 6. Gotchas

**Assigning a `DelayGenerator` to the retry strategy silently disables `Retry-After` handling.** It is honoured by default. Also, generator output ignores `MaxDelay`.

**AMR is TLS-only on port 10000.** `ssl=true`, and `abortConnect=false` so a transient startup failure doesn't permanently poison the multiplexer.

**One `ConnectionMultiplexer` for the process,** registered as a singleton. Creating one per operation exhausts sockets within minutes.

**The Redis write must not be able to fail the request.** The fetch already succeeded and the caller is waiting on prices; wrap the `marketdata:last:*` write so a Redis outage is logged and swallowed. `Fetch_RedisUnreachable_StillReturnsThePrice` pins it.

**Fetching N tickers is N HTTP calls** — Finnhub has no batch endpoint. Issue them with bounded concurrency rather than sequentially, or a twenty-position dashboard waits on twenty round trips one after another.

**`.Select()` after `.Include()` silently ignores the Include.** In the dashboard query there is no `Include` at all — project directly.

**`HybridCache` is still a distraction**, for a different reason than before. It is a read-through cache, and this phase deliberately does not do read-through — the provider is asked first and Redis is only the failure path.

The `BackgroundService`, `PeriodicTimer` and scoped-service-in-a-singleton traps **moved to Phase 4** with the poller they describe. They are real and they are simply not this phase's problem any more.

---

## 7. Your call

### How stale is too stale to show — `MarketData.Application/LastKnownPrice.cs`

```csharp
internal sealed class LastKnownPrice
{
    // TODO(you): the provider is down and you have a price from N minutes ago.
    //            Show it, or show nothing?
    //
    //   Always show it, with its age — simplest, and the reader decides. A price
    //     from Friday is the correct answer at 03:00 on a Sunday. A price from
    //     Friday is misleading on Tuesday afternoon, and nothing in the UI
    //     distinguishes those two cases except the timestamp you render.
    //
    //   Cap it — hide anything older than, say, an hour. Protects against the
    //     misleading case and recreates the blank table you were avoiding.
    //
    //   Cap it by market session — show anything since the last close. Correct,
    //     and needs the trading calendar this design has otherwise avoided.
    //
    // This is the whole of Phase 6's degradation story: whatever you pick here is
    // what a reviewer sees when they kill the provider.
    private bool IsWorthShowing(LastPrice? price) => …;
}
```

~8 lines. Write it before `Dashboard_ProviderDown_ShowsLastKnownWithAge`, since the test encodes the answer.

---

## 8. Done when

- [ ] `docker compose up` with **no** `Finnhub__ApiKey` → startup logs "using FakeQuoteProvider", dashboard shows prices
- [ ] Add a brand-new ticker → it has a price on the **very first render**, no pending state
- [ ] KPI tiles and the totals row agree with the per-row numbers
- [ ] Weights sum to 100%
- [ ] **Kill the provider and refresh** → the table still shows every position with its last known price and an amber age, no 500, no blank table
- [ ] Kill the provider *and* flush Redis → positions still list, prices and P&L blank, totals footnoted, still no 500
- [ ] Kill **Redis** with the provider up → prices are fresh and nothing is degraded; only the fallback is lost
- [ ] `redis-cli GET marketdata:last:AAPL` returns a value after one dashboard load
- [ ] No `BackgroundService`, `PeriodicTimer` or `IHostedService` anywhere in `src/` — those arrive in Phase 4
- [ ] `dotnet test` green, including `Dashboard_ProviderDown_ShowsLastKnownWithAge`
- [ ] `npm test` green
- [ ] Deployed **with a real `FINNHUB_API_KEY` secret**; the dashboard on GitHub Pages shows genuine prices, and the health panel names Finnhub rather than Fake
- [ ] README: why the dashboard asks the provider directly rather than reading a cache · the last-known-price fallback and the staleness call from §7 · why `dp` is not used for thresholds
- [ ] Table → cards at 375px, totals still legible
