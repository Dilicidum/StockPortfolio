# Phase 3 — Live prices & P&L · 0.8 days

## 1. Goal

The dashboard shows real prices, per-position and total value / cost / profit in currency **and** percent, position weight, and honest freshness timestamps. A ticker added seconds ago is priced on its first render, because the request fetches it. When the provider is unreachable the table still shows the last price it saw, with its age.

**No background service ships in this phase.** The poller and the price window are alert infrastructure and arrive in Phase 4; see §2.6.

Covers P0 reqs 2, 5 and 6, plus the backend half of req 10.

---

## 0. Corrections — read this before §2 and §5

**Phase 3 has shipped, and this spec did not survive contact with the tree.** Fifteen of its statements were
wrong, plus two more found during execution. Every one is corrected in
[phase-3-implementation.md](phase-3-implementation.md) §0 and carried into that file's tasks; the affected
statements below are marked in place rather than rewritten, because **a reversal that does not show what it
reversed is how the next reader follows a rule the code does not obey.**

The five worth knowing before reading anything else:

| # | This file says | What shipped |
|---|---|---|
| 1 | `IQuoteProvider` and `Quote` live in `MarketData.Domain` (§2.1) | Abstractions live in `.Application/Abstractions`. A port in `.Domain` makes `.Domain` the thing `.Infrastructure` implements, which reverses the onion |
| 6 | An all-zero `/quote` response means an unknown symbol (§2.2) | It means *no price this cycle*, identical to a fetch failure. Finnhub returns `c: 0` for a healthy symbol it blipped on too, so `/quote` cannot discriminate them **in principle**. Existence moved to `/search` with an exact match |
| 8 | The degradation table, written as provider-up or provider-down (§2.5) | The fallback is **per ticker**, computed as `requested − returned` after the call returns. Three of twenty failing is the likely failure and the table had no row for it |
| 10 | `.Select(h => new HoldingRow(h.Ticker.Value, h.Quantity, h.AveragePrice))` (§2.7) | Project `h.AveragePrice.Amount` and `.Currency`. The implementation plan's stated reason — that the complex type will not translate — is **itself wrong**; it translates fine. The real reason is keeping `Money`'s constructor off the per-row materialisation path |
| 15 | Resilience defaults (§2.2) | `CircuitBreaker.MinimumThroughput` ships at 100 and can never trip on a twenty-ticker dashboard, and the options validator is startup-fatal — a bad timeout pair takes down `docker compose up`, the P0 gate |

The remaining twelve are §2.1/§2.5's bare `Ticker` (MarketData declares its own; they meet as `string`),
§2.2's `decimal?` field list (all seven are optional, not just `d`/`dp`), §2.2 and §6 on `Retry-After` (a
Polly fact, not a Finnhub fact), §2.7's `IMarketDataQueries` (it is `IQuoteReader`), §4's Bicep additions
(**all already in the tree — the delta is zero lines**), §5's placement of the P&L tests (a pure calculator,
because no fakes exist), §5's `Fetch_RedisUnreachable_StillReturnsThePrice` (`FireAndForget` does not satisfy
it), §7's `internal sealed` `LastKnownPrice` in `.Application` (`public static` in `.Domain`), §3's route
(it already existed as a Phase 1 placeholder), and §3's `Intl.NumberFormat` instruction (pass the string
straight to `format()`; `Number(money.amount)` reintroduces the loss the string serialisation exists to
prevent).

**Two more that only execution could find**, and they are recorded here because both are the kind that pass
review:

- **§2.11 of the implementation plan proposed `/quote` with a non-null `c` as the cheapest existence check.**
  It shipped as `/search` with an exact case-insensitive match on `result[].symbol` — never `count > 0`,
  because `/search` is fuzzy. The reason is not cost: an all-zero `/quote` is returned for a non-existent
  symbol *and* for a healthy one Finnhub blipped on, so no reading of that response can tell them apart, and
  the check would have marked a valid holding unknown after one bad second. The `SymbolAnswered` result case
  the old design needed was deleted.
- **`Dashboard_ProviderReturns429_Returns200NotError` (§5) did not discriminate.** It passed under the
  rejected `try { provider } catch { redis }` implementation, because the good prices are written to
  `marketdata:last:*` before the discard and the wrong implementation re-read the numbers the same request had
  just stored — same symbols, same amounts, same count, only `IsLastKnown` differing. It now asserts
  `IsLastKnown == false` on the served symbols. A test that can go red is not the same as a test that can go
  red on the mistake it is named after.

⚠️ **What is *not* verified.** `az` is not installed on the development machine, so `az bicep build` and
`az deployment group what-if` have never run **locally** and §4's "zero Bicep lines" is expected rather than
confirmed. Note this does not block deploying: `deploy.yml` installs Bicep and runs `what-if` in the runner,
and fires on push to `main` — see
[the deployment record](../superpowers/specs/2026-08-02-azure-deployment-design.md), which also documents
the live deployment that has been running since 2026-08-02.
And §8's "adding a genuinely non-existent symbol returns `UnknownTicker`" needs a real `FINNHUB_API_KEY`,
which is not set — the mapping is unit-tested and the check can now return false, but the live path is
unexercised. `finnhub.io` was unreachable from the environment this plan was written in, so the
60-calls-per-minute free-tier figure is a search snippet rather than their documentation. It is stated as
inferred in the README and must not be laundered into fact.

---

## 2. Backend

### 2.1 Provider abstraction — ~~`MarketData.Domain`~~ `MarketData.Application/Abstractions`

> ⚠️ **CORRECTED — §0 items 1 and 4.** The heading was wrong and so is the `Ticker` in the snippet.
> `IQuoteProvider` lives in `.Application/Abstractions` beside every other port in the repo; the layer table
> assigns abstractions there, and `.Infrastructure` and `.Api` "meet only through `.Application/Abstractions`".
> A port in `.Domain` makes `.Domain` the thing `.Infrastructure` implements. `Quote` and `Ticker` *are*
> `.Domain` — but MarketData declares its **own** `Ticker`, copied from Portfolio's rather than referenced:
> reaching `Portfolio.Domain` is a rule-1 violation the architecture suite fails on. The two modules meet as
> raw `string` across `.Contracts`, canonicalised on both sides, with a unit test per module pinning
> `"aapl"` → `"AAPL"` and an `Ordinal` dictionary comparer so a canonicalisation divergence shows as a visible
> miss rather than hiding. The shipped `IQuoteProvider` also carries `string Name { get; }`, which the health
> endpoint and the integration fixture both need.

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

⚠️ Their docs are inconsistent: the response-attributes table lists seven fields and omits `t`; the sample shows `t` but omits `d`/`dp`. The live API returns all eight. ~~Model `d` and `dp` as `decimal?`.~~

> ⚠️ **CORRECTED — §0 item 5.** **All seven are optional, not two.** Finnhub's own OpenAPI `Quote` schema has
> no `required:` list and types every one of `c,h,l,o,pc,d,dp` as `number/float`. So `c` and `pc` are
> `decimal?` too — and a **missing** `c` must stay distinguishable from `c == 0`, which is the whole of the
> next correction. The machine-readable contract omits `t` as well, so the inconsistency runs deeper than the
> doc pages: guard the magnitude rather than trusting it (10 digits is seconds, 13 is milliseconds).

⚠️ **`dp` is the wrong number for your threshold.** It is change versus the *previous session close*, not versus your window. Compute the threshold from your own observations. Say so in the README — it is exactly what an interviewer probes. *(This one held. `dp` is deserialised and ignored, and the README says why.)*

~~⚠️ **An all-zero response (`c: 0, pc: 0`) means an unresolvable or unentitled symbol**, not "market closed". Outside market hours `/quote` returns the last trade values with a frozen `t`. Map all-zero to `UnknownTicker`, not to a $0 price.~~

> ⚠️ **CORRECTED — §0 item 6, and it is the correction with the most behaviour behind it.** All-zero maps to
> **"no price this cycle"**, identical to a fetch failure, and the symbol falls back to its last known price.
> It was never verified, and the only primary evidence points the other way:
> [Finnhub-API#54](https://github.com/finnhubio/Finnhub-API/issues/54) reports intermittent all-zero responses
> for AAPL, TSLA and FB. As originally written, one upstream blip would permanently mark a valid holding
> unknown. Entitlement failures actually surface as **401 *or* 403** — both, with the same
> `{"error":"…"}` body — and neither is ever retried.
>
> The consequence for the existence check is structural, not a matter of preference: since `c: 0` is returned
> both for a symbol that does not exist and for one Finnhub blipped on, **no reading of a `/quote` response
> can distinguish them.** The check ships as `/search?q=` with an exact case-insensitive match on
> `result[].symbol` — never `count > 0`, because `/search` is fuzzy and `q=AAP` returns AAPL — and it **fails
> open**: a null answer means the provider could not answer, and a Finnhub outage must not reject a purchase.

Resilience via `Microsoft.Extensions.Http.Resilience` on the typed client: retry, circuit breaker, timeout.

> ⚠️ **CORRECTED — §0 item 15. The defaults are decorative and the validator is startup-fatal.**
> `CircuitBreaker.MinimumThroughput` ships at **100**, so the breaker can never open for a twenty-ticker
> dashboard; it is configured to 10. And `HttpStandardResilienceOptionsCustomValidator` registers with
> `AddOptionsWithValidateOnStart`, so `AttemptTimeout > TotalRequestTimeout`, or
> `SamplingDuration < 2 × AttemptTimeout`, **takes the host down at boot** — i.e. takes down
> `docker compose up`, the P0 gate. Shipped: attempt 5 s, total 15 s, 2 retries, sampling 30 s. Do not set
> `HttpClient.Timeout` — the handler sets it to `InfiniteTimeSpan` and the pipeline owns timeouts.

⚠️ `Retry-After` on 429 is honoured **by default** — but **assigning a `DelayGenerator` silently disables it**. Do not set one. Better still, rate-limit client-side with `System.Threading.RateLimiting` so you never retry into a 429.

> ⚠️ **QUALIFIED — §0 item 7.** The rule is right and free, so keep it, but *"honoured by default"* is a
> **Polly fact, not a Finnhub fact**. There is no evidence Finnhub emits the header —
> [#122](https://github.com/finnhubio/Finnhub-API/issues/122) shows a free-plan 429 naming no headers at all —
> so the retry design must not depend on it arriving. The sentence's own second half is what actually carries
> the limit: a single **process-wide** `TokenBucketRateLimiter`, 25 tokens replenishing at 1/second. It must
> be registered as its own singleton and injected, **not** held as a field on the provider:
> `AddHttpClient<TClient,TImpl>` registers the client transient, so a per-instance bucket is a fresh bucket
> per resolution — the cap would hold within one dashboard request and never across concurrent ones, and every
> abandoned bucket leaves a live replenishment timer nothing disposes.

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

> ⚠️ **CLARIFIED — §0 item 14, and "from whichever path fetched it" is the phrase that misleads.** Read
> literally it invites putting the write inside `FinnhubQuoteProvider`, which satisfies the sentence and
> breaks the requirement: with no API key the **fake** is the only provider, so nothing would ever populate
> `marketdata:last:*` on the P0 compose path — silently breaking the `redis-cli GET` check, the
> kill-the-provider drill and the integration test that warms the key through `/api/dashboard`. There is
> exactly **one writer**, `QuoteReader`, above both providers, so the fake and Finnhub paths record
> identically.
>
> Separately, `CommandFlags.FireAndForget` does **not** make the write best-effort. It only means the caller
> receives the default return value immediately; `RedisConnectionException` and `RedisTimeoutException` still
> surface while enqueuing. The write is `await`ed inside `try/catch (RedisException)` — which is also what
> makes `redis-cli GET marketdata:last:AAPL` non-racy for §8.

### 2.5 When Finnhub is unreachable

The dashboard asks Finnhub first, always. Only when that fails does it read `marketdata:last:{ticker}`, and
it renders whatever it finds **with its age**:

| Situation | What the user sees |
|---|---|
| Finnhub answers | fresh prices, no age marker |
| Finnhub down, a last price exists | that price, in amber, "6 minutes ago" |
| Finnhub down, never fetched this ticker | the position with price and P&L blank, excluded from totals |
| Finnhub and Redis both down | as above — two simultaneous failures do not deserve a third mechanism |

> ⚠️ **CORRECTED — §0 item 8. This table is written as if the provider is wholly up or wholly down, and that
> is the *least* likely real failure.** The common one is three of twenty tickers timing out or 429ing, and
> the table has no row for it. Implemented as the table reads — one `try { provider } catch { redis }` — 17
> good prices are discarded and replaced with 20 stale ones because one ticker failed.
>
> The fallback is therefore **per ticker, always, never per call**: a set difference computed after the
> provider call returns, `missing = requested − returned`, then one `MGET` for the missing set. The
> whole-provider-down case falls out for free as the degenerate case where `returned` is empty. The rows the
> table should have had:
>
> | Failure | Absorbed where | Result |
> |---|---|---|
> | Provider wholly down (DNS, circuit open) | per-item `catch` in the fan-out → empty list | every ticker falls to last-known |
> | 3 of 20 return 429 after retries | same per-item `catch` | **17 fresh, 3 fall back** |
> | Redis read throws | `catch (RedisException)` around the `MGET` | those tickers get `null` price, excluded from totals |
> | Redis write throws | `catch (RedisException)` around the `SET`, logged | request unaffected |
> | Never fetched, provider down | nothing to read | `null` price, position still listed, footnoted |
>
> The per-item `catch` is the load-bearing line and must name the three concrete types
> (`HttpRequestException`, `TimeoutRejectedException`, `BrokenCircuitException`) rather than `Exception` — a
> `NullReferenceException` in the mapper must still fail loudly. Without it `Parallel.ForEachAsync` cancels
> the remaining work and faults on the first failure, turning one dead ticker into a blank dashboard: the
> exact inverse of this section.

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

`GetDashboard(UserId)` → ~~`DashboardDto`~~. Holdings from Postgres, prices via ~~`IMarketDataQueries` (MarketData's contract)~~, joined **in memory**. `AsNoTracking()`, projected straight to a DTO, never materialising the `Holding` aggregate.

> ⚠️ **CORRECTED — §0 items 2 and 3, and a naming rule.** The contract is **`IQuoteReader`**, which is what
> [module-boundaries.md](module-boundaries.md) and [module-interactions.md](module-interactions.md) both
> already named; `IMarketDataQueries` appears nowhere else in the repo and is a grab bag with the same failure
> mode as the `Errors.cs` the conventions ban — the next method lands there because the name permits anything,
> and Phase 4's window methods would force Portfolio to recompile when Alerts' needs change. Existence is a
> **second** interface, `ISymbolValidator`, because the two degrade in opposite directions: a price failure
> falls back to last-known, an existence failure must fail *open*.
>
> And the payload is **`GetDashboardResult`**, not `DashboardDto`. The CQRS naming rule is explicit that
> `<UseCase>Result` is the success payload record and the suffix is not optional; `grep -rn "Dto" src/` returns
> zero hits.

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
      .Select(h => new HoldingRow(h.Ticker.Value, h.Quantity, h.AveragePrice))   // CORRECTED — see below
      .ToListAsync(ct);
```

> ⚠️ **CORRECTED — §0 item 10, and the *reason* given there was itself wrong.** `AveragePrice` is a
> `ComplexProperty`, and the implementation plan said projecting it "will not translate". **It does.** EF Core
> documents complex types as projectable and translates them to their constituent columns; the one documented
> restriction is projecting a complex type through an *optional navigation*, which does not apply here. The
> shipped query projects `h.AveragePrice.Amount` and `h.AveragePrice.Currency` and rebuilds `Money` after
> materialisation anyway — **for a different reason**: `Money`'s constructor calls `ToUpperInvariant()`, EF
> binds a complex type's own constructor for materialisation exactly as it does an entity's, and that
> allocation would then run on every row of every `SELECT`. It is a breach already recorded in `CLAUDE.md`,
> and this is the query that would have paid for it.
>
> A wrong reason attached to a right instruction is worse than either alone, because it travels: the next
> reader avoids a translation problem that does not exist and misses the materialisation cost that does.
>
> Two smaller things in the same snippet: `GetVisibleHoldingsAsync` did not exist on `HoldingQueries` (which
> had exactly one method, `HoldsAsync`), and the row also needs `h.Id`. The reader is a **separate
> abstraction**, `IDashboardHoldingReader`, not a method on `IHoldingRepository` — that interface's own doc
> comment promises "every write method here commits before it returns" and its reads return *tracked*
> aggregates, which is the opposite of what a dashboard read wants. `Holding.IsVisible` does exist and is
> always `true` until Phase 5, so the filter is a no-op that costs nothing now and stops being one then.

⚠️ The two cross-module reads this section used to show — every ticker anyone holds, and who holds a given
ticker — are **not here any more**. The first belongs to the poller and the second to alert evaluation, both
of which moved to Phase 4, and both are sourced from Alerts rather than Portfolio now that alerts carry
their own ticker subscriptions. See [module-boundaries.md](module-boundaries.md).

Proving it is the job of a test and a README line rather than of hand-written SQL — see §5 and Phase 6's README checklist.

---

## 3. Frontend

### Route

`src/routes/_authenticated/dashboard.tsx` — mockup's Dashboard, minus hero and ticker strip.

> ⚠️ **CORRECTED — §0 item 12.** This is a **rewrite, not a new file.** The route already existed as a
> 43-line Phase 1 placeholder, already in `routeTree.gen.ts`, with four hardcoded `—` tiles captioned "Phase
> 2" — and that stale caption was itself a correction this phase owed. Two existing Phase 1 tests mount this
> route (`auth.test.tsx`, `sessionPersistence.test.tsx`) under MSW's `onUnhandledRequest: 'error'` with no
> default handlers, so the moment the route starts fetching, both go red for reasons that have nothing to do
> with the dashboard. A shared MSW handler fixes both.

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

The API returns money as strings. ~~Parse with~~ `Intl.NumberFormat` for display; never do arithmetic client-side. Percent values come pre-computed.

> ⚠️ **CORRECTED — §0 item 13. "Parse" is exactly the wrong verb.** Pass the string **straight to**
> `Intl.NumberFormat(undefined, { style: 'currency', currency }).format(money.amount)` — `format()` accepts a
> string and keeps the precision. `Number(money.amount)` reintroduces the identical IEEE-754 loss the string
> serialisation exists to prevent, and `portfolio.tsx` was doing precisely that, so it had to be rewritten
> rather than copied.
>
> Two traps in the same area. `format('')` and `format(null)` both render `$0.00`, so a null price must be
> branched on **before** the formatter or §5's "renders as pending, not `$0.00`" passes on a fixture and fails
> in production. And percentages are appended as a literal `%` — **never** `style: 'percent'`, which
> multiplies by 100 and turns 20.00 into 2000%. `profitPercent` and `weight` cross the wire as **strings** for
> the same reason money does: a bare `decimal` emits a JSON number under `NumberHandling.Strict`, and it does
> not stop being a double because the units are percent.

### Pending and stale states

A `null` price renders as a muted "—" with a "pending" tooltip and is excluded from the totals with a footnote. A stale headline timestamp turns the freshness line amber.

---

## 4. Infrastructure delta

Small, but real.

> ⚠️ **CORRECTED — §0 item 9. The infrastructure delta is zero lines.** Both Bicep bullets below were
> **already in the tree** before this phase started: `redis.bicep` builds the AMR connection string *inside*
> the module and returns it `@secure()` (built there rather than read via `existing` + `listKeys()`, because
> ARM hoists the key lookup and a first deploy into an empty group fails with `ParentResourceNotFound`), and
> `containerapp-api.bicep` carries the `finnhub-api-key` secret and its env var behind `empty()` guards —
> because **an ACA secret with an empty value is rejected**, and that conditional-array pattern *is* the
> workaround. Do not regress it to an unguarded `value:`. The explicit `httpGet` probes are there too.
>
> ⚠️ And **none of it was verified this phase.** `az` is not installed on the development machine, so
> `az bicep build --file infra/main.bicep` has still never run locally and `az deployment group what-if` has
> never reported locally — though the template demonstrably compiles and applies, because it built the
> deployment that has been live since 2026-08-02
> ([record](../superpowers/specs/2026-08-02-azure-deployment-design.md)).
> Zero lines changed, so what-if *should* report no changes; that is a prediction, not a result. The
> one thing worth checking when it does run: a `@secure()` **module output** is valid Bicep but trips the
> `outputs-should-not-contain-secrets` linter class, and finding that out during a deploy is the expensive way.

**Bicep**
- ~~Add the AMR connection string as an ACA secret, referenced by `ConnectionStrings__Redis`~~ — already present
- ~~Add `Finnhub__ApiKey` as a secret (empty in the default parameter file → the fake provider activates)~~ — already present
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
| ~~`FinnhubResponse_AllZero_MapsToUnknownTicker`~~ → `FinnhubResponse_AllZero_IsNoPriceNotUnknownTicker` | **Renamed with §0 item 6.** Not a $0 price, and not an unknown ticker either — the same outcome as a fetch failure |
| `FinnhubTimestamp_ParsedAsSeconds` | Not milliseconds; a companion test rejects a millisecond-magnitude value |
| `FakeProvider_SameTickerSameMinute_SamePrice` | Determinism |
| `Fetch_WritesLastKnownPrice` | Every path that fetches records what it saw |
| `Fetch_RedisUnreachable_StillReturnsThePrice` | The cache write is best-effort and must never fail the request |
| `LastKnown_IsWorthShowing_EncodesTheStalenessCall` | §7's decision, whichever way it went — see the correction under §7 for why it became **three** cases |

The poller, window-claim, retention and coalescer tests are **gone from this phase** — they test things that
now ship in Phase 4, and two of them (`ReadThrough_*`) test a mechanism that no longer exists at all.

> ⚠️ **CORRECTED — §0 item 14.** `Fetch_RedisUnreachable_StillReturnsThePrice` is **not** satisfied by
> `CommandFlags.FireAndForget`, which only returns the default value immediately and still surfaces connection
> and backlog-timeout exceptions at the call site. The write is awaited inside `try/catch (RedisException)`.
>
> Two tests were added that this table does not have. **`FakeProvider_SameTickerSameMinute_SamePrice` must
> compare across two separate instances** — same-instance equality passes with `string.GetHashCode()` too,
> which is randomised per process, so the single-instance form would report green while the property it claims
> to pin (two replicas serve the same price) is false. And `Decode_CorruptValue_IsNoPriceNotAThrow`: a corrupt
> stored value must not 500 the dashboard at the exact moment the provider is already down.

### Unit — `Portfolio.UnitTests` (P&L)

> ⚠️ **CORRECTED — §0 item 11. These tests had nowhere to live as written.** `Portfolio.UnitTests` has no
> fakes, no mocking library exists anywhere in `tests/`, and no handler in any module is unit-tested
> (deferred item B4/B6 was never actioned). Either this phase built the repo's first fake repository or it
> extracted the thing being asserted. **It extracted it**: `DashboardCalculator` is pure — rows, prices and a
> passed-in `DateTimeOffset` in, the result record out; no repository, no `IQuoteReader`, no clock.
>
> Three arithmetic rules this table does not state and a reviewer will check:
>
> - **Weight excludes unpriced positions from the denominator**, and an unpriced position gets `weight: null`,
>   not `0`. Zero is a claim ("this is 0% of your portfolio"); the truth is "unknown". Priced weights then sum
>   to 100 ± rounding, so `Weight_SumsToOneHundredPercent` asserts a tolerance of `pricedCount × 0.005` — never
>   an exact 100, and never fudge the largest row to force it. (§8's checkbox says "sum to 100%" flatly; read
>   it with the tolerance.)
> - **`Totals.Cost` is summed over the same subset as `Totals.Value`** — priced positions only. If `Value`
>   excludes an unpriced TSLA row but `Cost` includes its $1,000, `Profit = Value − Cost` reports a $500 loss
>   on a portfolio that is up $500. That is the actual content of `Totals_ExcludeNullPricePosition`, and a
>   dedicated `Totals_CostExcludesUnpricedPositions` pins it.
> - **`observedAt` is when *this app* fetched, never Finnhub's `t`.** `t` is the last *trade* time and freezes
>   at Friday's close, so binding to it renders every weekend dashboard amber with "3 days ago" while the
>   provider is perfectly healthy — the degradation signal firing on the happy path. `isLastKnown` is the
>   amber trigger; `observedAt` is only the age shown beside it.
>
> `stalestObservedAt` is named in §2.7 and never defined: it is `min(observedAt)` over **priced** positions,
> `null` when nothing is priced. And §3's "materially staler than the rest" needed a threshold to be
> implementable — a row renders its own timestamp when it trails the newest observation by more than the
> refresh interval.
>
> `Money_SerialisedAsString_NotNumber` is a serialisation test rather than a calculator test, and is already
> satisfied for `Money` by the existing converter. The assertion that earns its place is the one over
> **`weight` and `profitPercent`**, and it belongs with the integration tests.

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

### How stale is too stale to show — ~~`MarketData.Application/LastKnownPrice.cs`~~ `MarketData.Domain/LastKnownPrice.cs`

> ⚠️ **ANSWERED, and the path and shape below are both wrong — §0 item 3.** `.Application` is `public` per the
> layer table, so an `internal sealed` type there could not be seen by `.Infrastructure`; the path also
> carries no feature-area folder. It ships as a `public static class` in `.Domain`.
>
> **The call: always show it, with its age.** A wall-clock cap hides Friday's close at 03:00 on Sunday, which
> is the *correct* price; a market-session cap needs the trading calendar this design deliberately dropped;
> and either cap recreates the blank table the fallback exists to prevent — the one thing a reviewer killing
> the provider will see.
>
> But that answer alone makes `IsWorthShowing` return `true` unconditionally, and "always true" is a test that
> cannot fail. So the method's honest job is not staleness at all — it is **integrity of the stored
> observation**: a price of zero or less is rejected (a corrupt write, and exactly the shape an all-zero
> upstream response would leave behind), and a timestamp more than five minutes in the future is rejected (a
> skewed replica). Age never disqualifies anything. That turns one untestable assertion into three cases that
> can each go red, and `LastKnown_IsWorthShowing_EncodesTheStalenessCall` is written as those three.


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
- [ ] README: why the dashboard asks the provider directly rather than reading a cache · the last-known-price fallback and the staleness call from §7 · why `dp` is not used for thresholds · **the free tier's 60-calls-per-minute ceiling and what it means for concurrent viewers** — twenty positions is twenty calls for one viewer at the 60 s default, and three concurrent viewers exhaust the budget. State the 60/minute figure as **inferred**: `finnhub.io` was unreachable when this was written, so it rests on a search snippet rather than on their documentation
- [ ] Table → cards at 375px, totals still legible
