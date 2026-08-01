# Phase 3 — Live prices & P&L · 1.1 days

## 1. Goal

The dashboard shows real prices, per-position and total value / cost / profit in currency **and** percent, position weight, and honest freshness timestamps. A newly added ticker gets a price **immediately**, not on the next cycle. It works on a Saturday.

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

### 2.4 Poller — `MarketData.Infrastructure/QuotePollingService.cs`

```csharp
internal sealed class QuotePollingService(
    IServiceScopeFactory scopes, TimeProvider time, ILogger<QuotePollingService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60), time);
        do
        {
            try { await RunCycleAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Poll cycle failed"); }   // ⚠️ see §6
        }
        while (await timer.WaitForNextTickAsync(ct));
    }
}
```

`TimeProvider` injected into `PeriodicTimer` is what makes the cadence testable — `FakeTimeProvider.Advance(60s)` runs exactly one cycle, deterministically, with no `Task.Delay` and no flakiness.

**Cycle:**

1. Claim the window: `SET marketdata:claim:{windowStart} 1 NX EX 120`. Lose → skip.
2. **Take the overlap guard** (see below). Fail → log and skip.
3. Load the poll set.
4. Fetch under a token-bucket limiter, spread across the cycle, bounded concurrency respecting the 30/sec cap.
5. Write observations, trim windows.
6. Release the overlap guard in a `finally`.

#### The overlap fix

The audit found a real bug in `Initial.md:66-68`. The claim key contains the window start, so it guarantees one winner **within** a window and nothing **across** windows. Because `:82` deliberately spreads calls across the cycle, a long cycle — 50 tickers, provider latency, any honoured `Retry-After` — is still in flight when the next window opens. That is a *different key*, so a different replica claims it and starts fetching immediately. Two replicas fetching at once, roughly double the call rate, and the 50-ticker budget on `:80` breaks.

Fix — a second, non-window-keyed guard:

```csharp
// Window claim: decides WHO polls this window.
var claimed = await db.StringSetAsync($"marketdata:claim:{windowStart:o}", "1",
                                      TimeSpan.FromSeconds(120), When.NotExists);
if (!claimed) return;

// Overlap guard: decides WHETHER any cycle is already running, anywhere.
var guard = await db.StringSetAsync("marketdata:cycle-inflight", instanceId,
                                    TimeSpan.FromSeconds(110), When.NotExists);
if (!guard) { log.LogWarning("Previous cycle still running; skipping"); return; }
try { … } finally { await db.KeyDeleteAsync("marketdata:cycle-inflight"); }
```

The TTL is the backstop for a process that dies mid-cycle. Note this in the README — finding and fixing a race in your own design is a better story than not having had one.

### 2.5 Poll set — read live, not cached

Every distinct ticker anyone holds, read at the start of each cycle:

```csharp
// MarketData.Contracts — MarketData declares what it needs
public interface IPollSetSource { Task<IReadOnlySet<Ticker>> GetAsync(CancellationToken ct); }
```

The host supplies the adapter, backed by `Portfolio.Contracts`. MarketData therefore depends on nothing, and the module graph stays acyclic — see [module-interactions.md](module-interactions.md) §1.

`Initial.md:74` gives MarketData its own table of distinct tickers, kept in step by subscribing to holding events. **That table is dropped.** It was duplicated state, and it was the reason for the event subscription, a periodic reconciliation pass, and a real failure mode: that event stream has no cursor and no replay, so a publish lost to a crash between commit and dispatch would diverge the two permanently, and the ticker would render pending forever with no error anywhere. Reading the set live removes the table, both handlers, the reconciliation, and the divergence — one query instead of four moving parts.

The cost is one `SELECT DISTINCT` per cycle against an indexed column, sixty times an hour. That is nothing, and it is always correct by construction.

⚠️ It crosses a module boundary, so it goes through the contract, never a cross-schema read — `marketdata_svc` has no `USAGE` on `portfolio` and a direct query fails with a permission error. That is the schema isolation working.

Note the poll set uses **all** holdings, not just visible ones. Phase 5's show/hide is a dashboard display filter; hiding a position must not stop its price being collected, or unhiding it would show a stale number.

### 2.6 Storage — Redis

One sorted set per ticker, `marketdata:prices:{ticker}`, scored by observation epoch-ms, member `"{epochMs}:{price}"`.

⚠️ The composite member is not decoration. Sorted-set members must be unique — if the member were just the price, a ticker hitting the same value twice would **update the existing entry's score rather than adding a new one**, silently erasing the earlier reading.

Trimmed on the same write, no cleanup job:

```csharp
await db.SortedSetAddAsync(key, $"{ms}:{price}", ms);
await db.SortedSetRemoveRangeByScoreAsync(key, 0, ms - RetentionMs);
```

Retention = longest configurable alert window + margin = **1h 1m**. At one observation per minute that's 61 entries per ticker.

**Validated at startup**: `retention > maxConfigurableWindow`, else throw. Without the guard, someone raises the window in config, nobody updates retention, and alerts stop firing with no error anywhere.

Redis runs append-only, `appendfsync everysec` — a restart loses about a second of observations rather than the whole window. Losing the window leaves Phase 4's alerts with nothing to compare against until it refills: up to an hour of silent blindness rather than a visible failure.

### 2.7 Read-through on cache miss

The mechanism that makes the Saturday demo work.

```csharp
public async Task<IReadOnlyDictionary<Ticker, PriceObservation?>> GetLatestAsync(
    IReadOnlySet<Ticker> tickers, CancellationToken ct)
{
    var found = await ReadNewestFromWindowsAsync(tickers, ct);
    var stale = tickers.Where(t => IsTooOld(found.GetValueOrDefault(t))).ToHashSet();
    if (stale.Count == 0) return found;

    var fetched = await _fetchCoalescer.FetchAsync(stale, ct);   // per-symbol in-flight dedupe
    await WriteObservationsAsync(fetched, ct);
    return Merge(found, fetched);
}
```

`_fetchCoalescer` holds a `ConcurrentDictionary<Ticker, Task<Quote>>` so ten concurrent dashboard loads produce **one** provider call per symbol. It shares the poller's token-bucket limiter — read-through must not be able to blow the rate budget.

This single mechanism fixes three separate audit findings:

| Finding | How |
|---|---|
| Blank dashboard outside market hours (`Initial.md:76` trading-hours gate) | The request fetches for itself |
| A just-added ticker showing `pending` until the next cycle (`Initial.md:110`) | Priced on first render |
| Permanent divergence from a lost `HoldingAdded` event | A ticker missing from the poll set still gets a price |

It makes the poller an **optimisation** rather than the only path to a price. The trading-hours gate can therefore stay — but ship it as a config flag defaulting to **off**, so the reviewer's first run polls unconditionally.

### 2.8 Dashboard query

`GetDashboard(UserId)` → `DashboardDto`. Holdings from Postgres, prices via `IMarketDataQueries` (MarketData's contract), joined **in memory**. `AsNoTracking()`, projected straight to a DTO, never materialising the `Holding` aggregate.

The in-memory join is the visible consequence of prices living in Redis: you cannot sort or filter by current value in the database. At twenty holdings that costs nothing; it would matter at thousands.

**Per position:** quantity, average price, current price (nullable), market value, cost, profit in currency and percent, weight, `observedAt`.
**Totals:** value, cost, profit in currency and percent, `asOf`, `stalestObservedAt`.

All money computed server-side in `decimal`.

⚠️ **Serialise money as strings.** `System.Text.Json` writes `decimal` as a JSON number and `JSON.parse` materialises it as an IEEE-754 double — the precision you computed server-side is destroyed at the boundary regardless. `Initial.md:108` gets the instinct right and stops one step short.

⚠️ **Weight must be computed server-side too.** The mockup has the column; computing `value / total` in the browser reintroduces exactly the float arithmetic the `decimal` rule exists to prevent.

A ticker with no price returns `null` and renders as **pending**, never `$0.00` — a zero would flow into the totals as a complete loss on that position.

**No raw SQL anywhere. EF Core only.** The brief says *«SQL-запити — сирі або через query builder, головна вимога — параметризація»* — raw or query builder, the requirement is parameterisation. EF Core makes parameterisation **structural** rather than merely conventional: there is no API surface that concatenates a value into command text, so the failure mode the brief is guarding against cannot be written.

The two set-based reads Portfolio exposes are plain LINQ:

```csharp
public Task<List<string>> GetPollSetAsync(CancellationToken ct) =>
    db.Holdings.AsNoTracking().Select(h => h.Ticker.Value).Distinct().ToListAsync(ct);

public Task<List<Guid>> GetHoldersAsync(Ticker ticker, CancellationToken ct) =>
    db.Holdings.AsNoTracking().Where(h => h.Ticker == ticker)
              .Select(h => h.UserId.Value).ToListAsync(ct);
```

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

Polling is right here because prices arrive on a known schedule: you already know new data exists every 60 seconds, so asking is never wasted and never late. Push is reserved for Phase 4's alerts, where a breach can happen at any second.

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
- Add `MarketData__PollIntervalSeconds`, `MarketData__RetentionMinutes`, `MarketData__TradingHoursGateEnabled=false` as env vars
- API app already has `minReplicas: 1`; add a comment tying it to ingestion, since scaling to zero stops the poller

**Compose** — same env vars, pointing at the local Redis.

**Workflow** — no change.

⚠️ AMR is **TLS-only**. The connection string needs `ssl=true` and port `10000`, not `6379`. Local Redis in compose is plaintext on 6379, so the two connection strings differ in more than the host — parameterise the whole string, not just the hostname.

---

## 5. Tests

### Unit — `MarketData.UnitTests`

| Test | Asserts |
|---|---|
| `Poller_AdvanceOneInterval_RunsExactlyOneCycle` | `FakeTimeProvider.Advance(60s)`, provider called once |
| `Poller_ProviderThrows_ContinuesToNextCycle` | Two `Advance` calls, second cycle still runs |
| `Poller_CycleStillRunning_SkipsNextWindow` | The overlap guard — the bug this phase fixes |
| `WindowClaim_TwoInstances_OnlyOneWins` | Fake Redis or Testcontainers |
| `Retention_LessThanMaxWindow_ThrowsAtStartup` | The silent-failure guard |
| `SortedSetMember_SamePriceTwice_ProducesTwoEntries` | The composite-member pitfall |
| `ReadThrough_TenConcurrentRequests_OneProviderCall` | Coalescer works |
| `ReadThrough_FreshCacheEntry_NoProviderCall` | |
| `FinnhubResponse_NullDp_Deserialises` | Nullable `d`/`dp` |
| `FinnhubResponse_AllZero_MapsToUnknownTicker` | Not a $0 price |
| `FinnhubTimestamp_ParsedAsSeconds` | Not milliseconds |
| `FakeProvider_SameTickerSameMinute_SamePrice` | Determinism |

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
| `Dashboard_EmptyRedis_ReadThroughPopulatesAndReturnsPrices` | **The Saturday test** |
| `Dashboard_NewlyAddedTicker_HasPriceOnFirstRequest` | No pending state |
| `Dashboard_ProviderReturns429_ReturnsStaleDataNot500` | Degraded, not broken |
| `Dashboard_ProviderDown_ReturnsLastKnownWithOlderAsOf` | |
| `Dashboard_TickerWithNoPriceEver_ReturnsNullNotZero` | |
| `Dashboard_OnlyReturnsCallersHoldings` | |
| `PollCycle_WritesObservations_AndTrimsBeyondRetention` | Window bounded at 61 entries |
| `PollSet_ReflectsHoldingsImmediately_AfterAdd` | No cached ticker table to go stale |
| `PollSet_IncludesHiddenHoldings` | Phase 5's show/hide is display-only |
| `GetHolders_GeneratedSql_UsesParameterPlaceholder` | Capture the command text; it contains `@__ticker_0` and **not** the literal ticker value |

### Frontend

`renders totals from the API without client-side arithmetic` · `null price renders as pending, not $0.00` · `stale timestamp shows the amber freshness state` · `changing the interval control changes refetchInterval` · `provider error keeps the last good table on screen`

---

## 6. Gotchas

**An unhandled exception in a `BackgroundService` kills the whole host.** Since .NET 6 the default `BackgroundServiceExceptionBehavior` is `StopHost`. The in-loop `try/catch` is not defensive style — without it, one bad Finnhub response takes the API down and the dashboard 502s.

**Assigning a `DelayGenerator` to the retry strategy silently disables `Retry-After` handling.** It is honoured by default. Also, generator output ignores `MaxDelay`.

**`OperationCanceledException` on shutdown is not an error.** Catch it separately and break, or every graceful shutdown logs a scary stack trace.

**`PeriodicTimer.WaitForNextTickAsync` does not queue missed ticks.** A cycle that overruns means the next tick fires immediately, not that ticks accumulate. That is exactly why the overlap guard exists.

**Scoped services in a singleton.** `BackgroundService` is a singleton; resolve `DbContext` through `IServiceScopeFactory` per cycle. Holding one across cycles leaks tracked entities and eventually the connection.

**AMR is TLS-only on port 10000.** `ssl=true`, and `abortConnect=false` so a transient startup failure doesn't permanently poison the multiplexer.

**One `ConnectionMultiplexer` for the process,** registered as a singleton. Creating one per operation exhausts sockets within minutes.

**`HybridCache` is a distraction here.** It is a read-through cache over a serialised value; you need an ordered window with range trimming. Sorted sets, not `HybridCache`.

**`.Select()` after `.Include()` silently ignores the Include.** In the dashboard query there is no `Include` at all — project directly.

---

## 7. Your call

### Read-through staleness policy — `MarketData.Application/PriceReadThrough.cs`

```csharp
internal sealed class PriceReadThrough
{
    // TODO(you): when is a cached observation too old to serve?
    //
    //   Flat TTL (e.g. 90s) — simplest, and wrong at both edges. Mid-session, 90s
    //     is genuinely stale. At 03:00 on a Sunday, a Friday-close price is the
    //     correct and only answer, and refetching gets you the same number while
    //     the reviewer waits on a network round-trip.
    //
    //   Trading-hours-aware TTL — 90s while open, effectively infinite while closed.
    //     Correct, but reintroduces the trading-hours calendar you were trying to
    //     avoid depending on.
    //
    //   Serve-stale-and-refresh-in-background — instant render always, price is
    //     one request behind. Best UX, hardest to reason about in tests, and it
    //     means the very first load of a brand-new ticker still has nothing to serve.
    //
    // This decides whether a Saturday reviewer gets an instant dashboard or waits
    // on a fetch. It is the single most user-visible decision in this phase.
    private bool IsTooOld(PriceObservation? observation) => …;
}
```

~10 lines. Write it before `Dashboard_EmptyRedis_ReadThroughPopulatesAndReturnsPrices`, since the test encodes the answer.

---

## 8. Done when

- [ ] `docker compose up` with **no** `Finnhub__ApiKey` → startup logs "using FakeQuoteProvider", dashboard shows prices
- [ ] Add a brand-new ticker → it has a price on the **very first render**, no pending state
- [ ] Wait 60s → prices move, freshness line resets
- [ ] KPI tiles and the totals row agree with the per-row numbers
- [ ] Weights sum to 100%
- [ ] Set an invalid `Finnhub__ApiKey` → dashboard still renders with stale data and an amber freshness line, no 500
- [ ] Startup with `RetentionMinutes` below the max alert window → app **refuses to start** with a clear message
- [ ] `redis-cli ZCARD marketdata:prices:AAPL` ≤ 61 after an hour
- [ ] `dotnet test` green, including `Dashboard_EmptyRedis_ReadThroughPopulatesAndReturnsPrices` and `Poller_CycleStillRunning_SkipsNextWindow`
- [ ] `npm test` green
- [ ] Deployed; dashboard on GitHub Pages shows live prices from the Azure API
- [ ] README: the fake-provider default · the read-through policy you chose · why `dp` is not used for thresholds · the corrected window-claim overlap guard
- [ ] Table → cards at 375px, totals still legible
