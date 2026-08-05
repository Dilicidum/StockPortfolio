# Phase 3 — Implementation plan

Companion to [phase-3-live-prices.md](phase-3-live-prices.md). That file says *what* Phase 3 must do and
which traps to avoid. This one says *which files exist, in which project, referencing what, built in which
order* — the same relationship [phase-1-implementation.md](phase-1-implementation.md) has to
`phase-1-sign-in.md` and [phase-2-implementation.md](phase-2-implementation.md) has to
`phase-2-my-portfolio.md`.

**Goal:** log in, open `/dashboard`, and see every position priced — including one added ten seconds ago —
with value, cost, profit in currency and percent, weight, and an honest freshness stamp. Kill the provider,
refresh, and the table is still there in amber with the age of each price.

**Architecture:** MarketData becomes the third real module, built from the five shells that have been in the
solution since Phase 1. It adds three things neither Identity nor Portfolio has and therefore cannot teach:
an **outbound HTTP dependency**, a **second store** (Redis) that is allowed to fail, and a module with
**no database at all**.

**Tech stack:** three new NuGet packages, no new npm package, **zero Bicep changes**.

---

## 0. Read this first — the Phase 3 spec is wrong in fifteen places

`phase-3-live-prices.md` was last revised when the poller still lived in this phase, and parts of it predate
Phase 2's code. Fifteen of its statements do not survive contact with the tree, the architecture rules or the
Finnhub contract. Every one is corrected below and carried into the tasks. **Do not work from its §2 and §5
directly.**

| # | `phase-3-live-prices.md` says | Reality | Where fixed |
|---|---|---|---|
| 1 | §2.1 `IQuoteProvider` and `Quote` live in `MarketData.Domain` | Root `CLAUDE.md`'s layer table assigns **abstractions to `.Application`**, and says `.Infrastructure` and `.Api` "meet only through `.Application/Abstractions`". Every built precedent agrees: `Identity.Application/Abstractions/` has held `IPasswordHasher`, `IRefreshTokenRepository`, `ITokenIssuer` and `IUserRepository` since Phase 1, and `Portfolio.Application/Abstractions/IHoldingRepository.cs:6` followed in Phase 2. A provider port in `.Domain` also makes `.Domain` the thing `.Infrastructure` implements, which reverses the onion | §2.2 |
| 2 | §2.7 prices come "via `IMarketDataQueries` (MarketData's contract)" | `IMarketDataQueries` appears **nowhere else** in docs or code. [module-boundaries.md](module-boundaries.md):128 and [module-interactions.md](module-interactions.md):38 both name **`IQuoteReader`**. A module-named interface is a grab bag with the same failure mode as the `Errors.cs` the conventions already ban | §2.4 |
| 3 | §7 `internal sealed class LastKnownPrice` in `MarketData.Application/LastKnownPrice.cs` | `.Application` is **`public`** per the layer table — `internal` is `.Infrastructure`-only, so `.Infrastructure` could not see it. The path also carries no `<FeatureArea>/` folder | §2.2, Task 6 |
| 4 | §2.1 and §2.5 use a bare `Ticker` | MarketData has zero `.cs` files, so no `Ticker` exists there, and `Portfolio.Domain.Ticker` is off-limits — `ModuleBoundaryTests.Assembly_ReferencingAnotherModule_ReachesOnlyItsContracts` makes reaching it a test failure the moment MarketData gains its first type. `Portfolio.Domain/Ticker.cs:7` already says "Portfolio's own; other modules declare theirs" | §2.3 |
| 5 | §2.2 "Model `d` and `dp` as `decimal?`" | Finnhub's OpenAPI `Quote` schema has **no `required:` list** — all seven of `c,h,l,o,pc,d,dp` are optional, and every one is `type: number, format: float`. `c` and `pc` are `decimal?` too, and a **missing** `c` must be distinguishable from `c == 0` | §2.6, Task 7 |
| 6 | §2.2 "An all-zero response means an unresolvable or unentitled symbol… Map all-zero to `UnknownTicker`" | **Unverified, and the only primary evidence points the other way.** [Finnhub-API#54](https://github.com/finnhubio/Finnhub-API/issues/54) reports intermittent all-zero responses for AAPL/TSLA/FB; entitlement failures actually surface as **401/403** with `{"error":"You don't have access to this resource."}`. As written, one upstream blip permanently marks a valid holding unknown | §2.6 |
| 7 | §2.2 and §6 "`Retry-After` on 429 is honoured **by default**" | True, and it is a **`Microsoft.Extensions.Http.Resilience`/Polly fact, not a Finnhub fact**. No evidence Finnhub emits the header; [#122](https://github.com/finnhubio/Finnhub-API/issues/122) shows a free-plan 429 with no headers named. Keep the "never assign a `DelayGenerator`" rule — it costs nothing — but the client-side token bucket is what actually carries the limit | §2.6 |
| 8 | §2.5's degradation table | Written as if the provider is wholly up or wholly down. **That is the least likely real failure.** The common one is 3 of 20 tickers timing out or 429ing, and the table has no row for it. Implemented as the table reads — one `try { provider } catch { redis }` — 17 good prices get thrown away because one ticker failed | §2.5 |
| 9 | §4 "Add the AMR connection string as an ACA secret" · "Add `Finnhub__ApiKey` as a secret" | **Both already exist.** `infra/modules/redis.bicep:76` builds the AMR string inside the module and returns it `@secure()`; `containerapp-api.bicep:99-106,153-160` carry the `finnhub-api-key` secret and its env var, guarded by `empty()` because **an ACA secret with an empty value is rejected**. Phase 3's Bicep delta is **zero lines** | §2.9 |
| 10 | §2.7's query snippet `.Select(h => new HoldingRow(h.Ticker.Value, h.Quantity, h.AveragePrice))` | `AveragePrice` is a **`ComplexProperty`** (`HoldingConfiguration.cs:52-62`). Project `h.AveragePrice.Amount` and `.Currency` and rebuild `Money` after materialisation. ⚠️ **The reason first given here was wrong — see the correction below the table.** Also `GetVisibleHoldingsAsync` **does not exist** — `HoldingQueries.cs` has exactly one method, `HoldsAsync` | Task 12 |
| 11 | §5 puts `Position_*` / `Totals_*` / `Weight_*` in `Portfolio.UnitTests` | That project has **no fakes and no handler tests** — deferred item B4/B6 was never actioned, and no handler in any module is unit-tested. Either Phase 3 builds the repo's first fake repository, or those tests target a pure calculator. Build the calculator | §2.8, Task 13 |
| 12 | §3 "Route `src/routes/_authenticated/dashboard.tsx`" | The file **already exists** — 43 lines of Phase 1 placeholder, already in `routeTree.gen.ts`, with four hardcoded `—` tiles captioned "Phase 2". This is a rewrite, not a new file, and the stale caption is itself a correction Phase 3 owes | Task 20 |
| 13 | §3 "Parse with `Intl.NumberFormat`" | Pass the **string straight to `format()`**. `Number(money.amount)` reintroduces exactly the IEEE-754 loss the string serialisation exists to prevent — and `portfolio.tsx:86-91` does precisely that today, so it must be rewritten rather than copied. Separately `format('')` and `format(null)` both render `$0.00`, so a null price must be branched on **before** the formatter or §5's "renders as pending, not `$0.00`" fails in production while passing on a fixture | §2.10, Task 20 |
| 14 | §5 `Fetch_RedisUnreachable_StillReturnsThePrice` | `CommandFlags.FireAndForget` does **not** satisfy it. It only means the caller gets the default return value immediately; connection and backlog-timeout exceptions still surface at the call site. `await` the write inside `try/catch (RedisException)` — which also keeps `redis-cli GET marketdata:last:AAPL` (§8) non-racy | §2.7 |
| 15 | §2.2 "Resilience via `Microsoft.Extensions.Http.Resilience`… retry, circuit breaker, timeout" | The **defaults are decorative and the validator is startup-fatal**. `CircuitBreaker.MinimumThroughput` is 100, so the breaker can never open for a twenty-ticker dashboard; and `HttpStandardResilienceOptionsCustomValidator` registers with `AddOptionsWithValidateOnStart`, so `AttemptTimeout > TotalRequestTimeout` or `SamplingDuration < 2 × AttemptTimeout` **takes `docker compose up` down** — the P0 gate | §2.6 |

> ⚠️ **Item 10's instruction is right and its *stated reason* was wrong.** This row said "projecting the
> complex type will not translate." **It translates.** EF Core documents complex types as projectable and
> translates them to their constituent columns; the only documented restriction is projecting one through an
> **optional navigation**, which `Holding.AveragePrice` is not. The instruction stands unchanged, because the
> real justification is a different and better one: `Money`'s constructor calls `ToUpperInvariant()`, EF binds
> a complex type's own constructor for materialisation exactly as it does an entity's, and projecting the
> whole `Money` would put that allocation on every row of every `SELECT` — a breach `CLAUDE.md` already
> records, and this dashboard query is the one that would have paid for it.
>
> Corrected rather than quietly fixed because the failure mode is specific: a wrong reason attached to a right
> instruction travels further than either alone. The next reader avoids a translation problem that does not
> exist, and does not know to avoid the materialisation cost that does — so the moment a query appears where
> the cost is irrelevant, they will "correct" this back.

### Nine more, from `docs/plan/` and `CLAUDE.md` rather than from the spec

These are documents Phase 3 disproves. Task 23 fixes them.

> ✅ **All actioned in the Task 23 pass**, with three deviations recorded there: the count of dangling links is
> **ten references across eight files**, not nine; the `00-overview.md` section was **not** renamed back
> (renaming `## Four modules` to `§"Three modules, not four"` would restore the anchors by making the heading
> a lie), so all ten references were repointed instead; and three of the stale comments live in **code and
> compose files** that the documentation pass was scoped out of — they are listed as outstanding under Task 23.

- **`CLAUDE.md` Deployment: *"Phase 3 must put `minReplicas` back to 1"* is stale.** The poller moved to
  Phase 4 (`phase-3-live-prices.md:107,111,207`), `infra/main.bicep:250` stays at **0**, and the §8 checkbox
  "no `BackgroundService`, `PeriodicTimer` or `IHostedService` anywhere in `src/`" is what enforces it.
  `module-interactions.md:200,225` carries the same stale `minReplicas: 1` in its deployment diagram.
  **Fix the documents; do not action the line.**
- **`CLAUDE.md` contradicts itself on the module count.** `:11` says "the module count went from four to
  three"; `:41` says "Four modules — `Identity`, `Portfolio`, `MarketData`, `Alerts`"; `:45` says the merge
  "was reversed". On disk there are **three** module folders and no `Alerts` anywhere, and
  `ModuleBoundaryTests.cs:17` pins it: `ExpectedNames.Length.ShouldBe(17, "Three modules times five layers,
  plus Shared.Kernel and Shared.Api.")`. Alerts-as-a-module is a **decision recorded but not built** — it is
  Phase 4's first task, and `:17` becomes 22 then.
- **The project count is 24, not 23.** `CLAUDE.md:9,11` and `phase-1-implementation.md:11` all say 23. 19
  under `src/` + 5 under `tests/` = 24, and `StockPortfolio.slnx` lists exactly 24. The uncounted one is
  `Portfolio.UnitTests`, added by Phase 2. Phase 3 takes it to **25**.
- **`deferred-work.md` item E1 is marked RESOLVED and is not.** It closes on the grounds that Alerts is a
  module again; no Alerts module was ever built, so every orphan it tracked is still there —
  `db/init/01-roles.sql:57-59,84,138-145`, `docker-compose.yml:43,127`, `.env.example:39`,
  `containerapp-api.bicep:126`, `ci.yml:129`, `deploy.yml:202,222,314`. `module-boundaries.md:237-239` makes
  the same false claim. **Reopen it.** Phase 3 does not clean it up — see §7.
- **`README.md:277-278` says "Portfolio and MarketData are empty shells."** Portfolio has 35 `.cs` files.
- **Nine dangling links to `00-overview.md` §"Three modules, not four"**, a section that is now
  `## Four modules`: `phase-1-implementation.md:11,523`, `phase-2-implementation.md:31,4916`,
  `phase-1-sign-in.md:17`, `phase-2-my-portfolio.md:74`, `phase-5-make-it-mine.md:17`,
  `phase-6-doesnt-break.md:288`, `phase-4-alerts.md:137`, `deferred-work.md:176`.
- **The connection-budget arithmetic disagrees four ways.** `CLAUDE.md:190` "3 roles × 2 replicas requests
  600"; `00-overview.md:149` "× 4 roles × 2 = 800"; `README.md:263` "2 × 3 × 2 = 12"; `er-diagram.md:153`
  "2 × 4 × 2 = 16". Count what actually opens a pool rather than what is defined: `db/init/01-roles.sql`
  creates **four** service roles plus `migrator`, and `DatabaseInitialisedWaitStrategy.cs:14-21` pins that at
  five — but `Program.cs:57,60` registers **two** contexts, so exactly **two Npgsql pools exist per replica**.
  At `Maximum Pool Size=2` and `maxReplicas: 2` that is 2 × 2 × 2 = **8**, and `migrator` runs as a separate
  job, not concurrently with the API. All four published figures are wrong, in both directions.
- **`er-diagram.md:121` still says "Catch `23505` and route to the merge path."** `phase-2-implementation.md`
  §2.6 removed that catch and explains that the retry as written never terminates. `er-diagram.md:175` also
  cross-references `phase-3-live-prices.md` §2.4 for the window-claim keys; §2.4 is now "Recording what was
  fetched" and says the claim keys moved to Phase 4.
- **`HealthCheckExtensions.cs:35-37`'s comment is now wrong.** It says "Phase 3 puts price windows, alert
  cooldowns and SSE tickets here." §2.6 moved the windows to Phase 4 and the tickets belong to Alerts. Phase
  3's only Redis use is `marketdata:last:*`.

---

## 1. Scope

Brief **P0 reqs 2, 5 and 6**, plus the backend half of **req 10**.

Phase 3 **does** build: the MarketData module end to end, the dashboard endpoint and its P&L arithmetic, the
last-known-price fallback, the SPA dashboard, and the symbol-existence check that `AddHoldingCommandHandler`
has been carrying a TODO for since Phase 2.

Phase 3 does **not** build: the quote poller, `marketdata:prices:{ticker}`, the window claim keys, alert
evaluation, SSE, visibility toggling, BYOK, or i18n. `minReplicas: 0` therefore stays correct for one more
phase, and there is still no `BackgroundService` anywhere in `src/`.

---

## 2. Decisions settled before any code

Eleven decisions the spec left open, got wrong, or never knew it had to make.

### 2.1 DECISION — MarketData has no `DbContext`, no schema and no migration

Phase 3 persists exactly one thing and it is not in Postgres: `marketdata:last:{ticker}`. The spec says so
outright — "That is the whole mechanism" (`phase-3-live-prices.md:73`). The only `marketdata_*` block in
`er-diagram.md` is `marketdata_user_api_keys` (`:71-77`, related at `:106`, counted in its "Seven tables" at
`:111`) — that is BYOK, it is the only MarketData table named anywhere, and it belongs to **Phase 5**
(`module-boundaries.md:126`). Nothing this phase persists goes to Postgres.

Building an empty `MarketDataDbContext` to satisfy the shape rule costs a migration with zero tables, a
`marketdata.__EFMigrationsHistory` row, an `AddMarketDataModule` line in `Migrator/MigratedModules.cs:20-21`,
and turns `MigrationTests.cs:64` (`historySchemas.ShouldBe(["identity", "portfolio"])`) red — for no
behaviour. **Do not add MarketData to `MigratedModules.cs`.**

So root `CLAUDE.md`'s *"One `DbContext` and one Postgres schema per module, each connecting as its own role"*
needs an **explicit stated exception**, because a rule with a silent violation reads as an oversight and the
next reader fixes it. Task 23 rewrites it as: *one DbContext and one schema per module **that persists
anything**.*

What happens to the plumbing that already exists for a context that will not:

| Artefact | Action | Why |
|---|---|---|
| `ConnectionStrings:MarketData` — `appsettings.json:17`, `docker-compose.yml:126`, `main.bicep:196`, `containerapp-api.bicep:121-124` | **leave, untouched** | Inert; nothing reads it. Removing it means re-verifying the P0 compose gate plus a Bicep round trip, and Phase 5 puts it back |
| `Migrator/Program.cs:28` `["ConnectionStrings:MarketData"] = migratorConnectionString` | leave, with a one-line comment that it is Phase 5 forward cover | Dead but harmless |
| `marketdata` schema + `marketdata_svc` role — `db/init/01-roles.sql:53-55,83,128-135` | leave | Same argument. `DatabaseInitialisedWaitStrategy.cs:14-21` already asserts all five roles exist, and `MigrationTests.cs:28` already expects four schemas |
| `Migrator/StockPortfolio.Migrator.csproj:15` → `MarketData.Infrastructure` | leave | Removing and re-adding it in Phase 5 costs the same, and touching the Migrator's project graph means re-verifying the compose gate |
| Deferred item **C7** (readiness probes one of three roles) | **stays deferred, and say so** | MarketData has no database, so it contributes no `AddDbContextCheck<T>()`. Phase 3 does not move C7 and should not leave it looking triggered |
| `MarketData.Infrastructure.csproj:4-5` — `Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` | **delete** | A module with no context that references EF is an invitation to add one "since it's already there" |

### 2.2 DECISION — layer placement, and two places the spec puts things in the wrong project

| Type | Project | Accessibility |
|---|---|---|
| `Ticker` (MarketData's own), `Quote`, `LastPrice` | `.Domain` | `public` |
| `LastKnownPrice` (the §7 staleness rule) | `.Domain` | `public` |
| `IQuoteProvider`, `ILastKnownPriceStore`, `IQuoteNudge` | `.Application/Abstractions` | `public` |
| `QuoteReader`, `SymbolValidator` (the contract implementations) | `.Application` | `public` |
| `FinnhubQuoteProvider`, `FakeQuoteProvider`, `RedisLastKnownPriceStore`, `FinnhubOptions`, `FakeQuoteOptions` | `.Infrastructure` | **`internal sealed`** |
| `MarketDataModule` | `.Infrastructure` | `public` — the one public type there |
| `MarketDataEndpoints` | `.Api` | `public` |

`IQuoteProvider` moves out of `.Domain` (§0 item 1) and `LastKnownPrice` moves out of `.Application`
(§0 item 3). Both moves are forced twice over: by `CLAUDE.md`'s layer table, and by the integration tests —
`Api.IntegrationTests` must construct a `DeadQuoteProvider` to swap in through `ConfigureTestServices`, which
requires `IQuoteProvider` to be `public` **and** reachable from a project the test assembly can see.

**The typed `HttpClient` registration goes in `MarketData.Infrastructure`, and that is legal.** `.Api` cannot
hold it — `ApiAssembly_ReferencesNeitherPersistenceNorItsOwnInfrastructure` forbids `.Api` from seeing
`FinnhubQuoteProvider` at all. And `InfrastructureAssembly_ReferencesNoAspNetCore` stays green because the
predicate is exactly

```csharp
private static bool IsAspNetCore(string? name) =>
    name is not null && name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal);
```

and no such name appears among `MarketData.Infrastructure`'s **own emitted references**. The package closure
is clean too — `Microsoft.Extensions.{Http, Http.Diagnostics, Resilience, Telemetry, ObjectPool}` plus
`Polly.*`, whose nearest miss is `Microsoft.Extensions.ObjectPool`, built in the aspnetcore repo but **not
named for it**.

⚠️ **The rule could not have caught it anyway, and that is worth knowing before you lean on it.**
`FindForbiddenReferencePath` only enqueues a reference for further walking when it passes `IsFirstParty`
(`SolutionAssemblies.cs:128`, requiring the `StockPortfolio.` prefix), so no third-party graph is ever
inspected. Rule 4 constrains **our** project graph, not the package graph — a NuGet package that itself
dragged in ASP.NET Core would sail past it. The package closure being genuinely clean is the load-bearing
fact; the rule is the guard on our own edges. If rule 4 ever does go red, read the reported path and fix the
reference — **never** weaken the predicate to make it pass.

### 2.3 DECISION — MarketData declares its own `Ticker`, and the two meet as `string`

Not a style preference: `ModuleBoundaryTests.Assembly_ReferencingAnotherModule_ReachesOnlyItsContracts` flags
any reference from `MarketData.*` to `Portfolio.Domain`, and that case is **currently skipped** because
MarketData is an empty shell. It goes live by itself with MarketData's first type.

They meet as **raw `string` across `.Contracts`**, canonicalised on both sides. The precedent is exact and
already shipped: `Portfolio.Contracts/IUserHoldsTicker.cs:7` takes `string ticker`, and `HoldingQueries.cs:16-19`
re-parses it through `Ticker.Create(...).TryPickT0` and returns `false` on garbage.

The cost is two regexes that can drift, and the drift is invisible — it surfaces as a dashboard row that
never matches a price, not as a compile error. Two guards: a unit test per module pinning `"aapl"` → `"AAPL"`,
and **key the returned dictionary with `StringComparer.Ordinal`, not `OrdinalIgnoreCase`**. Both sides are
already canonical, so `Ordinal` turns a canonicalisation divergence into a visible miss instead of hiding it.

### 2.4 DECISION — `MarketData.Contracts` ships `IQuoteReader` and `ISymbolValidator`, not `IMarketDataQueries`

`IMarketDataQueries` (§0 item 2) is a grab bag: the next method lands there because the name permits anything,
and Phase 4's window methods would land on the same interface, forcing Portfolio to recompile when Alerts'
needs change. `IPriceWindowReader` is Phase 4's and must **not** be declared now — an interface with no
implementation is a shell no architecture rule catches.

Existence is split off rather than added as a method, **because the two degrade in opposite directions**: a
price failure falls back to last-known, whereas an existence failure must fail **open** or a Finnhub outage
blocks adding holdings. Different failure policy, different interface.

```csharp
namespace StockPortfolio.Modules.MarketData.Contracts;

/// <summary>One price for one symbol; IsLastKnown marks a value read from the fallback store.</summary>
public sealed record QuotedPrice(string Ticker, decimal Price, DateTimeOffset ObservedAt, bool IsLastKnown);

public interface IQuoteReader
{
    /// <summary>Asks the provider first, falls back to the last recorded price. A symbol with no price at
    /// all is absent from the result rather than present with zero. Keys are canonical upper case, Ordinal.</summary>
    Task<IReadOnlyDictionary<string, QuotedPrice>> GetCurrentPricesAsync(
        IReadOnlyCollection<string> tickers, CancellationToken ct);
}

public interface ISymbolValidator
{
    /// <summary>Whether the provider recognises this symbol. Returns true when the provider cannot answer —
    /// a purchase must not be rejected because Finnhub is down.</summary>
    Task<bool> IsKnownSymbolAsync(string ticker, CancellationToken ct);
}
```

`decimal`, not `Money`. `Money` lives in `Shared.Kernel` so a reference would technically be legal, but the
convention is records of primitives, MarketData does no arithmetic, and `Money`'s constructor calls
`ToUpperInvariant()` on every construction (a breach `CLAUDE.md` already records). Portfolio wraps into
`Money.Usd(...)` where the P&L is computed.

`IsLastKnown` is a bool the consumer **cannot derive from `ObservedAt`** — a fresh provider answer for a
thinly traded symbol also has an old timestamp, and conflating those is how amber ends up on a healthy row.

`MarketData.Contracts.csproj` is `<Project Sdk="Microsoft.NET.Sdk">` with no ItemGroup at all. **Keep it that
way** — zero references is what makes `ContractsAssembly_ReferencesNoPersistence` trivially green once it
stops skipping.

### 2.5 DECISION — the fallback is per ticker, always, never per call

§0 item 8. Implement it as a **set difference computed after the provider call returns**:

```
missing = requested − returned          →  MGET marketdata:last:{t} for each t in missing
```

The whole-provider-down case then falls out for free as the degenerate case where `returned` is empty.
Implemented the way §2.5's table reads — one `try { provider } catch { redis-for-everything }` — 17 good
prices are discarded and replaced with 20 stale ones because one ticker 429'd.

Corollary for the test table: `Dashboard_ProviderReturns429_Returns200NotError` must fail **some** tickers,
not all, or it does not distinguish the two implementations. Add
`Dashboard_PartialProviderFailure_MixesFreshAndLastKnown` — that is the test that actually pins this.

> ⚠️ **Necessary but not sufficient — found during execution, and it is the more interesting half.** Failing
> one of three tickers and asserting the other two came back with their prices **still does not discriminate.**
> The rejected implementation passes it. The reason is ordering: `QuoteReader` writes every quote the provider
> returned to `marketdata:last:*` *before* the set difference is computed, so a `try { provider } catch
> { redis-for-everything }` that discards the two good prices then re-reads them out of the store the same
> request had only just written. Same symbols, same amounts, same `pricedPositionCount`. Every assertion on a
> **number** is satisfied by the implementation the number was chosen to reject.
>
> The only observable difference is `IsLastKnown`, and the test now asserts it is **false** on the served
> symbols. The generalisation is worth carrying past this phase: *a fallback path that reproduces the happy
> path's values is invisible to any assertion on values.* "Can this test go red?" is the wrong question when
> the answer is yes and it still cannot go red **on the mistake it is named after**. It is a subtler form of
> the failure `CLAUDE.md`'s "a test that cannot fail is worse than no test" already records, and it is written
> up there beside it.

Full failure matrix, every row returning **200**:

| Failure | Absorbed where | Result |
|---|---|---|
| Provider wholly down (DNS, circuit open) | per-item `catch` in the fan-out → empty list | every ticker falls to the last-known read |
| 3 of 20 return 429 after retries | same per-item `catch` | **17 fresh, 3 fall back** |
| Redis read throws | `catch (RedisException)` around the `MGET` | those tickers get `null` price, excluded from totals |
| Redis write throws | `catch (RedisException)` around the `SET`, logged | request unaffected |
| Never fetched, provider down | nothing to read | `null` price, position still listed, footnoted |

### 2.6 DECISION — the Finnhub contract, corrected in four places

Verified against Finnhub's own OpenAPI spec (vendored in `Finnhub-Stock-API/finnhub-go`), their official
Python client, and their issue tracker. **`finnhub.io` itself is unreachable from this environment** (the
agent proxy denies CONNECT), so nothing below was read off their doc pages directly — see §7.

| Claim | Status |
|---|---|
| 30 API calls/second cap on all plans; over-limit → **429** | **CONFIRMED**, verbatim from the rate-limit page via search |
| Free tier = 60 calls/minute | CONFIRMED weakly — snippet only, no contradicting figure anywhere |
| No batch endpoint | **CONFIRMED** — 105 paths in the spec, zero named `symbols`. N tickers = N calls |
| `t` present on the wire but absent from the schema | **CONFIRMED** — and stronger than the spec claims: the machine-readable contract omits `t` too |
| `t` is UNIX **seconds** (the WebSocket feed is ms) | CORROBORATED, not confirmed. **Guard it**: a 10-digit value is seconds, 13 is milliseconds. One line, and it removes the dependence on this being right |
| Only `d`/`dp` nullable | **CORRECTED** — no `required:` list; **all seven optional** |
| All-zero ⇒ unknown ticker | **CORRECTED** — map all-zero to *"no price this cycle"*, identical to a fetch failure. Reserve `UnknownTicker` for an explicit 401/403 |
| `Retry-After` on 429 | **UNVERIFIED for Finnhub.** Keep the no-`DelayGenerator` rule; do not let the retry design depend on the header arriving |
| Sandbox retired | **CONFIRMED** — Wednesday **21 September 2022** |
| Bad/unentitled key → 403 | **CORRECTED** — 401 *and* 403 both occur with the same body. Treat them identically and never retry either |
| Base URL | Prefer **`https://api.finnhub.io/api/v1/`** (what their own client hardcodes) over the spec's `finnhub.io/api/v1` — the marketing host sits behind a WAF |
| Error body | `{"error":"<message>"}`, single key |

Two more the spec does not mention and a .NET client needs: set a real **`User-Agent`** (a default .NET UA is
a common WAF trigger — their client sends `finnhub/python`), and their own `/quote` description says
*"Constant polling is not recommended."* Phase 3 does not poll, so that is consistent; it becomes a live
concern in Phase 4 and belongs in the README beside the SSE decision matrix.

**Resilience configuration is not the default.** The standard pipeline order is literal code, outermost to
innermost: `RateLimiter → TotalRequestTimeout → Retry → CircuitBreaker → AttemptTimeout`. Ship:

```csharp
.AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout       = TimeSpan.FromSeconds(5);
    o.TotalRequestTimeout.Timeout  = TimeSpan.FromSeconds(15);
    o.Retry.MaxRetryAttempts       = 2;
    o.CircuitBreaker.MinimumThroughput = 10;    // the shipped 100 can never trip on a 20-ticker dashboard
    o.CircuitBreaker.SamplingDuration  = TimeSpan.FromSeconds(30);
    // NEVER assign o.Retry.DelayGenerator — see below.
});
```

⚠️ **`ShouldRetryAfterHeader` is not a flag; its setter *is* the `DelayGenerator` assignment.** Setting a
generator overwrites the Retry-After one and nothing warns. `MaxDelay` is also ignored for generated delays.

⚠️ **The validator is startup-fatal.** `AttemptTimeout > TotalRequestTimeout`, or
`SamplingDuration < 2 × AttemptTimeout`, fails `AddOptionsWithValidateOnStart` and takes the host down at
boot — i.e. `docker compose up`, the P0 gate. The values above satisfy both (5 < 15; 30 ≥ 10).

### 2.7 DECISION — the Redis write is `await`ed inside a `try/catch`, not fire-and-forget

§0 item 14. `CommandFlags.FireAndForget` only means the caller receives the default return value
immediately; `RedisConnectionException` and `RedisTimeoutException` still surface while enqueuing. The catch
is what satisfies `Fetch_RedisUnreachable_StillReturnsThePrice`, and awaiting is what makes
`redis-cli GET marketdata:last:AAPL` non-racy for the §8 checklist.

Key and encoding:

```
marketdata:last:{TICKER}   ->   "{price}:{epochMs}"        e.g. "187.4200:1780000000000"
```

**Not JSON**: two fields, no schema evolution planned, and `redis-cli GET` returning a directly readable value
*is* a checklist item. **Not a hash**: the read side is decisive — the fallback reads up to N keys at once, and
a string type makes that **one `MGET`** where N hashes need a pipeline of N `HGETALL`s.

```csharp
static string Encode(decimal price, DateTimeOffset at) =>
    string.Create(CultureInfo.InvariantCulture, $"{price}:{at.ToUnixTimeMilliseconds()}");
```

`decimal.ToString()` with no format specifier round-trips — `decimal` carries its own scale, so `187.4200m`
writes `"187.4200"` and parses back to the same scale. `InvariantCulture` is explicit even though
`Directory.Build.props` sets `InvariantGlobalization=true`: the explicit culture is what survives someone
flipping that flag, and a comma decimal separator here corrupts every stored price silently.
**A failed decode is "no last-known price", never an exception** — a corrupt entry must not 500 the dashboard
when the provider is already down.

⚠️ **`StackExchange.Redis` 3.x reshaped `StringSetAsync`.** The modern overload takes
`(RedisKey, RedisValue, Expiration, ValueCondition, CommandFlags)`; the 2.x `TimeSpan?/When` forms still exist
but are `[EditorBrowsable(Never)]` and will not appear in IntelliSense.

### 2.8 DECISION — the P&L arithmetic lives in a pure calculator, not in the handler

§0 item 11. `Portfolio.UnitTests` has no fakes, no mocking library exists anywhere in `tests/`, and no handler
in any module is unit-tested. §5's seven `Position_*` / `Totals_*` / `Weight_*` tests therefore have nowhere to
live unless Phase 3 either builds the repo's first fake repository or extracts the thing being asserted.

**Extract it.** `DashboardCalculator` in `Portfolio.Application/Dashboard/` takes rows and prices and returns
the DTO — no repository, no `IQuoteReader`, no clock beyond a passed-in `DateTimeOffset`. That keeps the
fake-repository decision (deferred B4/B6) out of this phase, and it is the thing the tests are actually about.

Three arithmetic rules the spec does not state and that a reviewer will check:

- **Weight excludes unpriced positions from the denominator.** `weight = marketValue / Σ(marketValue over
  priced positions)`; an unpriced position gets `weight: null`, not `0`. Zero is a claim ("this is 0% of your
  portfolio") and the truth is "unknown". Priced weights then sum to 100.00 ± rounding, so
  `Weight_SumsToOneHundredPercent` asserts a tolerance of `pricedCount × 0.005` — never an exact 100, and
  never fudge the largest row to force it.
- **`Totals.Cost` is summed over the same subset as `Totals.Value`** — priced positions only. If `Value`
  excludes an unpriced TSLA row but `Cost` includes its $1,000, `Profit = Value − Cost` reports a $500 loss on
  a portfolio that is up $500. This is the actual content of `Totals_ExcludeNullPricePosition`.
- **`observedAt` is when *this app* fetched the quote, not Finnhub's `t`.** Finnhub's `t` is the last *trade*
  time; outside market hours it is frozen at Friday's close, so binding `observedAt` to it renders every
  weekend dashboard amber with "3 days ago" while the provider is perfectly healthy — the degradation signal
  firing on the happy path, which is the most visible way to fail §8. `isLastKnown` is the amber trigger;
  `observedAt` is only the age shown beside it.
- **`stalestObservedAt` needs a definition, because the spec names the field and never says what it is.**
  It is `min(observedAt)` over **priced** positions, or `null` when no position is priced. And §3's "a
  per-row timestamp where a position is **materially staler** than the rest" needs a threshold or it cannot
  be implemented: a row renders its own timestamp when `observedAt < asOf − 60s`. Neither rule drives colour
  — `isLastKnown` alone does that.

### 2.9 DECISION — the infrastructure delta is zero Bicep lines and one operational action

§0 item 9. Everything §4 asks for is already in the tree:

- `redis.bicep:76` — `'${cluster.properties.hostName}:${database.properties.port},password=…,ssl=True,abortConnect=False'`,
  built **inside** the module and returned `@secure()`, with the `ParentResourceNotFound` lesson written out
  at `:64-76`. Port 10000, `Microsoft.Cache/redisEnterprise`, `Balanced_B0`, HA disabled.
- `containerapp-api.bicep:99-106,153-160` — the `finnhub-api-key` secret and `Finnhub__ApiKey` env var, both
  behind `empty()` guards because **an ACA secret with an empty value is rejected**. That conditional-array
  pattern *is* the workaround; do not regress it to an unguarded `value: finnhubApiKey`.
- `containerapp-api.bicep:223-254` — explicit `httpGet` liveness and readiness probes on `/health/live` and
  `/health/ready`, port 8080. Confirmed necessary: ACA injects default TCP probes when ingress is on, and a
  TCP probe only proves a socket opened.
- `main.bicep:250` — `minReplicas: 0`, and it **stays** (§0).

The only real action is **setting `FINNHUB_API_KEY` as a real repository secret**. Until it is set, the public
URL serves invented prices for real tickers, which reads as broken rather than as a thoughtful fallback.

⚠️ The casing differs between the emitted string (`ssl=True,abortConnect=False`) and the docs (`ssl=true`).
`StackExchange.Redis` parses case-insensitively, so it is not a bug — **do not "fix" one to match the other**
in a Phase 3 diff.

⚠️ `bicep build` has never run locally. Run `az bicep build --file infra/main.bicep` once this phase: a
`@secure()` **module output** is valid Bicep but trips the `outputs-should-not-contain-secrets` linter class,
and finding that out during a deploy is the expensive way.

Cold start is the honest consequence of `minReplicas: 0`: from zero, the first dashboard request pays
container start **then** the N-call fan-out, serially. `refetchInterval` and `refetchOnWindowFocus` keep the
app warm for the rest of a session, so it is a first-load cost. Say it rather than leaving 0 silently.

### 2.10 DECISION — money crosses the wire as a string in both directions, and percentages do too

`MoneyJsonConverter` is already registered, so `Money` members serialise as strings for free. **Bare `decimal`
members do not** — under `NumberHandling.Strict` they emit JSON numbers and `JSON.parse` makes them doubles.
That is the identical trap §2.7 cites for money, and it does not stop being a double because the units are
percent.

So `profitPercent` and `weight` are **`string`**, formatted server-side as
`value.ToString("0.00", CultureInfo.InvariantCulture)`. It costs nothing, needs no converter (a
`decimal`-to-string converter would fight `NumberHandling.Strict`), and says at the signature that the value
is display-only. The client appends a literal `%` — **never** `Intl.NumberFormat` with `style: 'percent'`,
which multiplies by 100 and turns 20.00 into 2000%.

⚠️ **`JsonIgnore(Condition = Never)` on every nullable member is load-bearing, not decoration.**
`Program.cs:27` sets `DefaultIgnoreCondition = WhenWritingNull`, so without it a null price is **absent from
the JSON**, not `null` — and `Dashboard_ProviderDown_NeverFetchedTicker_ReturnsNullNotZero` would be asserting
something the wire never carries.

### 2.11 DECISION — `AddHoldingCommandHandler` gets its symbol lookup, and it fails open

Three separate comments in the tree say this is Phase 3's:

- `AddHoldingCommandHandler.cs:19` — `// Phase 2 validates shape only. Phase 3 swaps this for a provider lookup.`
- `PortfolioEndpoints.cs:125` — `// Phase 3 turns this into a real symbol lookup, and this is the line to revisit then.`
- `UnknownTicker.cs:3` — *"Phase 2 checks shape; Phase 3 checks existence."*

`UnknownTicker` exists for no other reason; leaving it shape-only leaves a result case whose name is a lie.
The failure record, the endpoint's named `unknownTicker =>` lambda and the OpenAPI metadata **all already
exist** — no new status code, no new record, no `/openapi/v1.json` change.

Name the trade the spec never does: **this puts an outbound HTTP call on a write path.** One extra call per
add against a 60/min budget is nothing; a Finnhub outage rejecting valid purchases is not, and it converts a
degraded read into a broken write. That is why `IsKnownSymbolAsync` returns **`true` on provider failure**,
and why it is a separate interface from `IQuoteReader` (§2.4).

~~Cheapest implementation is a `/quote` call asserting a non-null `c` — the same call you were going to make.~~
`/search?q=` is **fuzzy** (`q=AAP` returns AAPL), so if you use it, existence must be an exact
case-insensitive match on `result[].symbol`, **never `count > 0`**. `/stock/symbol?exchange=US` returns the
entire US listing per call and is the wrong tool.

> ⚠️ **REVERSED during execution. It ships as `/search` with an exact case-insensitive match, not `/quote`.**
>
> The `/quote` recommendation above and §0 item 6 genuinely conflict, and the conflict is not a matter of
> emphasis. Item 6 establishes that Finnhub returns `c: 0` — present, and zero — both for a symbol that does
> not exist *and* for a healthy symbol it blipped on ([Finnhub-API#54](https://github.com/finnhubio/Finnhub-API/issues/54)).
> If that is true, then **no reading of a `/quote` response can discriminate the two cases**, and "assert a
> non-null `c`" is not a cheap approximation of an existence check — it is not an existence check at all. It
> answers *unknown* to every transient failure, which is precisely the outcome item 6 exists to prevent, on
> the one path where the answer is destructive: a rejected purchase.
>
> `/search` can discriminate, because a non-existent symbol is genuinely absent from the results rather than
> present with a zero. The extra call is one per add against a 60/minute budget, which is the trade §2.11
> already accepted.
>
> **`SymbolAnswered` was deleted.** It existed only to carry the third state `/quote` forced —
> *the provider replied but the reply says nothing* — and with `/search` there is no such state: a null
> response means the provider could not answer, and the check fails open. A result case that can never be
> produced is a case every `.Match` call site still has to name.
>
> The two-in-one appeal ("the same call you were going to make") is what made the wrong option look cheap, and
> it is worth naming as a pattern: the calls are not the same call. One asks *what is this worth*, whose
> failure mode is falling back to a stale price; the other asks *does this exist*, whose failure mode is
> rejecting a valid write. §2.4 already split them into two interfaces for exactly that reason, and this
> paragraph then quietly rejoined them at the HTTP layer.

⚠️ `FakeQuoteProvider` must accept any well-shaped ticker, or every existing `HoldingsTests` case using an
invented symbol goes red. The consequence: the fake never produces `UnknownTicker`, so this swap is exercised
against the real provider only — cover it with a **unit** test over the response mapping, not an integration
test.

---

## 3. Global constraints

**Three new packages, pinned. No floating ranges.**

Anchor the hunk on the **last** line of the `Infrastructure` group (`Directory.Packages.props:25`) — the
group opens at `:20` and holds four other entries first, so a patch anchored on the `<ItemGroup>` line will
not apply:

```diff
     <PackageVersion Include="StackExchange.Redis" Version="3.1.0" />
+    <!-- Phase 3. Verified against nuget.org on 2026-08-05. 10.8.0 is the dotnet/extensions train
+         (same as TimeProvider.Testing below), NOT the 10.0.10 runtime train — two release lines,
+         both current. -->
+    <PackageVersion Include="Microsoft.Extensions.Http" Version="10.0.10" />
+    <PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.8.0" />
+    <!-- Pinned explicitly: it arrives transitively only via Polly.RateLimiting, which asks for
+         >= 8.0.0, so without this line the resolved version is Polly's floor rather than a
+         number recorded here. -->
+    <PackageVersion Include="System.Threading.RateLimiting" Version="10.0.10" />
   </ItemGroup>
```

Stated so the resolution is a decision rather than an accident: `Microsoft.Extensions.Resilience 10.8.0`
requires `Polly.Extensions >= 8.4.2` and `Polly.RateLimiting >= 8.4.2`, so NuGet resolves **8.4.2**
(lowest-applicable) while the latest stable is 8.7.0. Transitive pinning is on, so naming `Polly.Core` /
`Polly.Extensions` / `Polly.RateLimiting` would upgrade them everywhere. Leave them at 8.4.2 unless something
demands otherwise.

`StackExchange.Redis` stays at **3.1.0** even though 3.1.3 shipped 2026-08-03. Moving is a separate,
deliberate decision, not a side effect of this phase.

**No `IOptions<T>`.** There is zero `IOptions` usage in `src/`; the established pattern is a POCO built
eagerly and registered as a singleton (`services.AddSingleton(JwtOptions.FromConfiguration(config))`).
`FinnhubOptions.FromConfiguration` and `FakeQuoteOptions.FromConfiguration` follow it — and
`FinnhubOptions.FromConfiguration` **must not throw on a missing key**.

**`CA1848` is a build error** under `TreatWarningsAsErrors`. Every log call is `[LoggerMessage]`
source-generated; precedent at `src/Api/Middleware/ApiExceptionHandler.cs:12-16`.

**Bounded concurrency:** `Parallel.ForEachAsync` with `MaxDegreeOfParallelism = 4`, plus a
`TokenBucketRateLimiter` (`TokenLimit = 25`, `TokensPerPeriod = 1`, `ReplenishmentPeriod = 1s`,
`AutoReplenishment = true`).

⚠️ **The bucket must be a singleton, and it must not ride on the typed client.**
`AddHttpClient<TClient, TImpl>` registers the client **transient**, so a limiter held as a field on
`FinnhubQuoteProvider` is a *fresh bucket per resolution* — the 30/sec cap would then be enforced only
within one dashboard request and never across concurrent ones, and every abandoned bucket leaves a live
replenishment timer nothing disposes. Register it separately and inject it:

```csharp
services.AddSingleton(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
{
    TokenLimit = 25, TokensPerPeriod = 1,
    ReplenishmentPeriod = TimeSpan.FromSeconds(1), AutoReplenishment = true,
    QueueLimit = 256, QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
}));
```

The arithmetic below assumes exactly one shared bucket; without it none of it holds. The arithmetic for
20 positions: 5 waves at ~250 ms ≈ **1.25 s wall time**, peak
**16 calls/sec** — under the 30/sec cap, and the bucket cannot release more than 25 at once so the burst cap
is structurally satisfied regardless of `MaxDop`. The binding constraint is the other one: **20 of the free
tier's 60 calls/minute for one user's one dashboard**. At a 60 s refresh that is a third of the budget for a
single viewer; three concurrent viewers exhaust it. That is a documented property of the free tier, not a bug
to engineer around — put it in the README beside the `dp` note.

⚠️ Raising `MaxDop` to 20 buys nothing: the bucket queue serialises the excess anyway, so wall time is
unchanged and you hold 20 sockets instead of 4.

⚠️ **For unit tests set `AutoReplenishment = false`** and call `TryReplenish()`. The auto-replenishment timer
takes no `TimeProvider`, so `FakeTimeProvider` cannot advance it.

---

## 4. File map

**New — `MarketData.Domain` (4 files)**

```
Ticker.cs                     public readonly partial record struct; public static Create → OneOf<…>
Quote.cs                      public readonly record struct Quote(Ticker, decimal, DateTimeOffset)
LastPrice.cs                  public readonly record struct LastPrice(decimal, DateTimeOffset)
LastKnownPrice.cs             public static class; IsWorthShowing(LastPrice?, DateTimeOffset)
```

⚠️ **No `AssemblyInfo.cs` here.** The two that exist (`Shared.Kernel`, `Shared.Api`) carry a `CA1716`
suppression *because the assembly name contains the segment `Shared`, a VB keyword* — neither
`Identity.Domain` nor `Portfolio.Domain` has one, and the tree builds at 0 warnings.
`MarketData.Domain` has nothing to suppress. The `AssemblyInfo.cs` Phase 3 **does** need is
`MarketData.Infrastructure`'s, for a different reason — see below.

**New — `MarketData.Application` (5 files)**

```
Abstractions/IQuoteProvider.cs        GetQuotesAsync(IReadOnlySet<Ticker>, ct) · SymbolExistsAsync
Abstractions/ILastKnownPriceStore.cs  ReadAsync(IReadOnlyCollection<Ticker>) · WriteAsync(Quote)
Abstractions/IQuoteNudge.cs           Nudge(string, decimal, TimeSpan) — the dev hook's seam
Prices/QuoteReader.cs                 implements Contracts.IQuoteReader; owns the set-difference fallback
Prices/SymbolValidator.cs             implements Contracts.ISymbolValidator; fails open
```

**New — `MarketData.Contracts` (2 files)** — `IQuoteReader.cs` (with `QuotedPrice`), `ISymbolValidator.cs`.
No `.csproj` ItemGroup; keep it reference-free.

**New — `MarketData.Infrastructure` (7 files)**

```
MarketDataModule.cs                   the one public type; AddMarketDataModule(services, config)
Quotes/FinnhubQuoteProvider.cs        typed client, fan-out, per-item catch, best-effort write
Quotes/FinnhubQuoteResponse.cs        record of decimal? c,h,l,o,pc,d,dp + long? t
Quotes/FinnhubOptions.cs              FromConfiguration; HasApiKey; never throws
Quotes/FakeQuoteProvider.cs           FNV-1a seeded walk; implements IQuoteNudge
Quotes/FakeQuoteOptions.cs            volatility / drift
Prices/RedisLastKnownPriceStore.cs    MGET read, SET write, both inside try/catch (RedisException)
AssemblyInfo.cs                       [assembly: InternalsVisibleTo("…MarketData.UnitTests")], own file
```

⚠️ `InternalsVisibleTo`, **not** `CA1716`. Tasks 7–9 unit-test `FinnhubQuoteResponse`, `FakeQuoteProvider`
and `RedisLastKnownPriceStore`, all of which §2.2 declares `internal sealed`. The precedent is
`Portfolio.Infrastructure/AssemblyInfo.cs:4`, whose own comment says why it is in its own file: *"riding on
another type's file means deleting that type silently deletes this."*

**New — `MarketData.Api` (3 files)**

```
MarketDataEndpoints.cs                AddMarketDataApi, MapMarketDataEndpoints, the provider log line,
                                      GET /api/marketdata/health, POST /api/dev/nudge
Requests/NudgeRequest.cs              string Ticker, decimal Percent, int TtlSeconds
Validators/NudgeRequestValidator.cs   shape only, no I/O
```

**New — `Portfolio` (5 new files, 3 edited)**

```
Application/Abstractions/IDashboardHoldingReader.cs
Application/Dashboard/DashboardCalculator.cs
Application/Dashboard/Queries/GetDashboard/GetDashboardQuery.cs
Application/Dashboard/Queries/GetDashboard/GetDashboardQueryHandler.cs
Application/Dashboard/Queries/GetDashboard/GetDashboardResult.cs  (+ DashboardPosition, DashboardTotals)
Infrastructure/Persistence/HoldingQueries.cs        — EDITED, gains the visible-holdings read
Infrastructure/PortfolioModule.cs                   — EDITED, binds IDashboardHoldingReader
Infrastructure/DependencyInjection.cs               — EDITED, registers the dashboard handler
```

⚠️ **The success payload is `GetDashboardResult`, not `DashboardDto`.** `CLAUDE.md`'s CQRS naming section is
explicit — *"`<UseCase>Result` is the **success payload record**… the suffix is not optional"* — and
`grep -rn "Dto" src/ --include=*.cs` returns **zero hits** today. The shipped precedent is
`GetCurrentUserResult.cs`. `DashboardPosition` and `DashboardTotals` are supporting records in the same
use-case folder.

**New — `src/Api` (1 file)** — `Extensions/RedisExtensions.cs` (deferred item C4).

**New — tests** — `tests/StockPortfolio.Modules.MarketData.UnitTests/` (project + ~8 files);
`tests/StockPortfolio.Api.IntegrationTests/DashboardTests.cs`.

**New — SPA (6 files)** — `src/marketdata/dashboardApi.ts` · `src/lib/format.ts` ·
`src/components/StatTile.tsx` · `src/components/Freshness.tsx` · `src/components/ApiHealth.tsx` ·
`tests/dashboard.test.tsx`.

**Edited** — `Directory.Packages.props` · `StockPortfolio.slnx` ·
`Portfolio.Application.csproj` (+`MarketData.Contracts`) · `MarketData.Infrastructure.csproj` (−EF, −Npgsql,
+Redis, +Http, +Http.Resilience) · `src/Api/Program.cs` · `src/Api/Extensions/HealthCheckExtensions.cs` ·
`src/Api/appsettings.json` · `AddHoldingCommandHandler.cs` · `PortfolioEndpoints.cs` ·
`ModuleBoundaryTests.cs` · `ApiFixture.cs` · `Wire.cs` · `EndpointMetadataTests.cs` ·
`Portfolio.Infrastructure/PortfolioModule.cs` · `Portfolio.Infrastructure/DependencyInjection.cs` ·
`src/Web/src/routes/_authenticated/dashboard.tsx` · `src/Web/src/routes/_authenticated/portfolio.tsx` ·
`src/Web/src/index.css` (a `--warn` token) · `src/Web/tests/msw/server.ts` ·
`src/Web/tests/auth.test.tsx` · `src/Web/tests/sessionPersistence.test.tsx` · `docker-compose.yml`
(one comment, and optionally D10) · `CLAUDE.md` · `README.md` · four `docs/plan/` files ·
`docs/deferred-work.md`.

**Not edited, deliberately** — `infra/**` (zero lines, §2.9) · both `Dockerfile`s (all five MarketData `COPY`
lines already exist, then both `COPY src/Modules/ src/Modules/` wholesale) · `docker-compose.yml`'s **env
block** (`Finnhub__ApiKey: ${FINNHUB_API_KEY:-}` already at `:139`; only a stale comment at `:65` changes) ·
`.env.example` (`FINNHUB_API_KEY=` at `:55`) · both workflows (`ci.yml:131` already pins
`FINNHUB_API_KEY: ''`; `deploy.yml:204,224` already pass it) · `tests/Directory.Build.props` · `Migrator/**`.

---

## 5. Tasks

### Task 1: Packages and the test project

- [ ] Add the three `PackageVersion` lines from §3.
- [ ] `MarketData.Infrastructure.csproj`: **delete** the EF Core and Npgsql references; add
      `StackExchange.Redis`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Http.Resilience` (versionless — CPM).
- [ ] Create `tests/StockPortfolio.Modules.MarketData.UnitTests/`, copying `Portfolio.UnitTests`' csproj and
      `GlobalUsings.cs`. Add it to `StockPortfolio.slnx` between the Identity and Portfolio unit-test entries.
      All five MarketData src projects are **already** in the slnx.
- [ ] `dotnet build` clean, `dotnet test` unchanged. **Project count 24 → 25.**

### Task 2: `MarketData.Domain` — `Ticker`, `Quote`, `LastPrice`

- [ ] `Ticker`: `public readonly partial record struct Ticker(string Value)` with a **`public static`** factory
      `Create(string?) → OneOf<Ticker, InvalidInput>`, canonicalising to upper case. The positional constructor
      stays public and guard-free — copy `Portfolio.Domain/Ticker.cs:8,14` exactly, **copy, do not reference**
      (§2.3). The factory cannot be private: `QuoteReader` in `.Application` and `FinnhubQuoteProvider` in
      `.Infrastructure` both construct tickers from outside `.Domain`, exactly as `HoldingQueries.cs:16` and
      `AddHoldingCommandHandler.cs:20` already do for Portfolio's.
- [ ] `Quote`, `LastPrice` as above. All guard-free constructors.
- [ ] **No `AssemblyInfo.cs`** — see §4. There is nothing here to suppress.
- [ ] ⚠️ **The architecture suite goes red here — on the first *type in a `StockPortfolio.*` namespace*, not
      the first file.** `IsEmptyShell` is
      `!assembly.GetTypes().Any(type => type.Namespace?.StartsWith("StockPortfolio", …) == true)`
      (`SolutionAssemblies.cs:92-94`), so a file carrying only assembly-level attributes leaves the assembly a
      shell. `Ticker.cs` is what flips `MarketData.Domain`. Task 3 is next for that reason.
- [ ] Tests: `Ticker_LowerCase_CanonicalisesToUpper`, `Ticker_TooLong_ReturnsInvalidInput`,
      `Ticker_Empty_ReturnsInvalidInput`.

### Task 3: The architecture lists — **staged, one assembly at a time**

The skip arithmetic, derived from source rather than copied from `CLAUDE.md`. The only `Assert.Skip` in the
suite is `ModuleBoundaryTests.cs:174`, reached from five call sites:

| Rule | Method | Runs over | Skips now | End of Phase 3 |
|---|---|---|---|---|
| 1 | `Assembly_ReferencingAnotherModule_ReachesOnlyItsContracts` | `ScannedNames` (17) | 6 | **1** |
| 2 | `ContractsAssembly_ReferencesNoPersistence` | 3 | 2 | **1** |
| 3 | `DomainType_ExposesNoPublicSetter` | 3 | 1 | 0 |
| 4 | `InfrastructureAssembly_ReferencesNoAspNetCore` | 3 | 1 | 0 |
| 5 | `ApiAssembly_ReferencesNeitherPersistenceNorItsOwnInfrastructure` | 3 | 1 | 0 |
| | | | **11** | **2** |

⛔ **Do not make both list edits here.** The five MarketData assemblies gain their first type at five
different tasks — `.Domain` at Task 2, `.Contracts` at Task 4, `.Application` at Task 6b, `.Infrastructure`
at Task 7, `.Api` at Task 17. Pruning `expected` to one entry now makes
`EmptyShells_AreExactlyThePhasesNotYetBuilt` red (four assemblies are still shells and `actual` will list
them), **and** adding all five to `populated` makes `PopulatedAssemblies_AreNotEmptyShells_…` red for the
same reason. That is two red tests from Task 3 to Task 17 — precisely the "build red for the whole phase"
this task exists to prevent.

**Each edit lands beside the task that populates its assembly**, one line at a time. The suite is green after
every task:

| Task | Assembly it populates | Edit | Skips after |
|---|---|---|---|
| 3 | `MarketData.Domain` (Task 2) | remove from `expected`, add to `populated` | 11 → **9** |
| 4 | `MarketData.Contracts` | same, one line | 9 → **7** |
| 6b | `MarketData.Application` | same, one line | 7 → **6** |
| 7 | `MarketData.Infrastructure` | same, one line | 6 → **4** |
| 17 | `MarketData.Api` | same, one line | 4 → **2** |

Task 3's own work:

- [ ] `ModuleBoundaryTests.cs:69-77` — remove **only** `"StockPortfolio.Modules.MarketData.Domain"` from
      `expected`. `ModuleBoundaryTests.cs:36-51` — add **only** `SolutionAssemblies.NameOf("MarketData",
      "Domain")` to `populated`. Skips 11 → 9 (rule 1 loses one, rule 3 loses its only one). Suite green.
- [ ] Rewrite the failure message at `:87-89`, which is already false today and stays false ("Rule 2 runs
      over `Portfolio.Contracts` alone and skips the other two").
- [ ] **Break rule 1 on purpose, and get the recipe right.** Rule 1 reads
      `assembly.GetReferencedAssemblies()` — emitted metadata — so a `using` alone is a compile error, not a
      red test, and a bare `Ticker` binds to MarketData's own type ahead of any using-directive, emits no
      reference, and reports **green** while proving nothing. Add the `<ProjectReference>` to
      `Portfolio.Domain` in `MarketData.Domain.csproj` **and** a fully-qualified field —
      `private static readonly StockPortfolio.Modules.Portfolio.Domain.Ticker Probe = default;` — watch
      `Assembly_ReferencingAnotherModule_ReachesOnlyItsContracts(…MarketData.Domain)` report
      `-> StockPortfolio.Modules.Portfolio.Domain`, then revert both.
- [ ] By the end of the phase `Architecture.Tests` moves 37 passed / 11 skipped → **46 passed / 2 skipped of
      48 discovered**, before any Phase 3 test is written. `ModuleBoundaryTests.cs:17`'s `ShouldBe(17, …)`
      does **not** change — that becomes 22 in Phase 4 when Alerts lands.

### Task 4: `MarketData.Contracts`

- [ ] `IQuoteReader.cs` and `ISymbolValidator.cs` exactly as §2.4. `QuotedPrice` beside `IQuoteReader`.
- [ ] Leave the csproj with no ItemGroup.
- [ ] The two list edits for `MarketData.Contracts` (Task 3's table). Skips 9 → 7.
- [ ] Rule 2 now runs over `Portfolio.Contracts` **and** `MarketData.Contracts` and passes for both.

### Task 5: `Portfolio.Application` gains the MarketData reference

- [ ] `Portfolio.Application.csproj` gains `<ProjectReference Include="…MarketData.Contracts.csproj" />`.
- [ ] `dotnet test` — rule 1 permits `<OtherModule>.Contracts`, and `CrossModuleRule_JudgesAReferenceAsExpected`
      already pins `Portfolio.Contracts → false`, so nothing should break. Confirm rather than assume.

### Task 6: `LastKnownPrice` — the §7 "your call", answered

**Always show it, with its age.** A wall-clock cap hides Friday's close at 03:00 on Sunday, which is the
*correct* price; a market-session cap needs the trading calendar this design deliberately dropped; and either
cap recreates the blank table the fallback exists to prevent — the one thing a reviewer killing the provider
will see.

But "always true" is a test that cannot fail, and `CLAUDE.md` forbids those. So the method's honest job is not
staleness — it is **integrity of the stored observation**:

```csharp
public static class LastKnownPrice
{
    /// <summary>Clock skew tolerated before an observation is treated as corrupt rather than future.</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Age never disqualifies a price — the reader judges that from the timestamp we render.</summary>
    public static bool IsWorthShowing(LastPrice? price, DateTimeOffset now) =>
        price is { } p && p.Price > 0m && p.ObservedAt <= now + FutureTolerance;
}
```

- [ ] `LastKnown_IsWorthShowing_EncodesTheStalenessCall` becomes three cases that can each go red: a five-day-old
      price **is** shown (the staleness call); a zero or negative price is not (a corrupt write, and exactly the
      shape Finnhub's all-zero responses would leave behind); a price stamped an hour in the future is not
      (a skewed replica). Use `FakeTimeProvider`.

### Task 6b: `MarketData.Application/Abstractions` — the three ports

Everything from Task 7 onward implements or injects one of these, so they must exist first. Nothing here has
an implementation yet and the build stays green.

- [ ] `IQuoteProvider` — `Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlySet<Ticker>, CancellationToken)`,
      `Task<bool> SymbolExistsAsync(Ticker, CancellationToken)`, and **`string Name { get; }`**. `Name` is
      needed twice over: by the health endpoint and panel (Task 17, Task 20) and by the integration fixture
      (Task 18), which cannot assert on a type that is `internal` in `.Infrastructure`.
- [ ] `ILastKnownPriceStore` — `ReadAsync(IReadOnlyCollection<Ticker>, CancellationToken)`,
      `WriteAsync(IReadOnlyCollection<Quote>, CancellationToken)`. Note the **collection** on the write: the
      caller is `QuoteReader`, not the provider (Task 10).
- [ ] `IQuoteNudge` — `void Nudge(string ticker, decimal percent, TimeSpan duration)`.
- [ ] All `public` (§2.2). The two list edits for `MarketData.Application`. Skips 7 → 6.

### Task 7: `FinnhubQuoteProvider` and its response mapping

- [ ] `FinnhubQuoteResponse` — `decimal? C, H, L, O, Pc, D, Dp` plus `long? T` (§2.6).
- [ ] Map: missing or null `c` → no quote. All-zero → **no quote**, not `UnknownTicker` (§0 item 6).
      401/403 → an auth failure that is never retried. Timestamp: digit-count guard, then
      `DateTimeOffset.FromUnixTimeSeconds`.
- [ ] Fan-out per §3, with the **per-item `catch`** as the load-bearing line:

```csharp
await Parallel.ForEachAsync(symbols,
    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
    async (symbol, token) =>
    {
        try
        {
            using var lease = await _budget.AcquireAsync(1, token);   // the SINGLETON bucket, §3
            if (!lease.IsAcquired) { LogBudgetExhausted(symbol); return; }
            if (await FetchOneAsync(symbol, token) is { } q) { quotes.Add(q); }
        }
        catch (HttpRequestException ex)       { LogQuoteFailed(ex, symbol); }
        catch (TimeoutRejectedException ex)   { LogQuoteFailed(ex, symbol); }
        catch (BrokenCircuitException ex)     { LogQuoteFailed(ex, symbol); }
    });
```

  Catch the three concrete types, **not `Exception`** — a `NullReferenceException` in the mapper must still
  fail loudly. Without the catch, `Parallel.ForEachAsync` cancels the remaining work and faults the task on
  the first failure, turning one dead ticker into a blank dashboard: the exact inverse of §2.5.
- [ ] ⚠️ **The provider does not write to Redis.** The store write moved to `QuoteReader` (Task 10) so that
      the fake and Finnhub paths record identically — see the note there. The provider fetches and returns,
      nothing else.
- [ ] `AssemblyInfo.cs` with `[assembly: InternalsVisibleTo("StockPortfolio.Modules.MarketData.UnitTests")]`
      **in its own file** (§4). Tasks 7–9 unit-test three `internal sealed` types; without this they cannot
      see them, and riding the attribute on another type's file means deleting that type silently deletes it.
- [ ] The two list edits for `MarketData.Infrastructure`. Skips 6 → 4.
- [ ] Tests: `FinnhubResponse_NullDp_Deserialises`, `FinnhubResponse_MissingC_IsNotAPrice`,
      `FinnhubResponse_AllZero_IsNoPriceNotUnknownTicker` (renamed from the spec's
      `…_MapsToUnknownTicker`), `FinnhubTimestamp_ParsedAsSeconds`, `FinnhubTimestamp_MillisecondMagnitude_IsRejected`,
      `Finnhub401And403_AreNotRetried`.

### Task 8: `FakeQuoteProvider`

- [ ] Deterministic per `(ticker, minute)`, continuous within a UTC day.
      **Never `string.GetHashCode()`** — randomised per process since .NET Core 2.1, so two replicas serve
      different prices and a restart changes them; `FakeProvider_SameTickerSameMinute_SamePrice` would pass
      inside one process while the property it claims to pin is false. Use a six-line FNV-1a.
- [ ] `basePrice = 20m + (hash(ticker) % 48000) / 100m` → $20–$500, stable forever.
      `minuteIndex = (int)((now − startOfUtcDay).TotalMinutes)`. Walk from `basePrice`, each step drawing
      `u = hash(ticker, m) / uint.MaxValue` and applying `price *= 1 + Drift + (u * 2 − 1) * Volatility`,
      clamped `≥ 0.01m`.
- [ ] Config `MarketData:Fake:VolatilityPerMinute` (default `0.002`) and `:DriftPerMinute` (default `0`).
- [ ] `SymbolExistsAsync` returns true for anything matching the ticker shape (§2.11).
- [ ] The walk jumps at UTC midnight. Honest for a fake and cheaper than carrying state — one-line comment,
      not a pretence otherwise.
- [ ] Tests: same-minute determinism **across two separate instances** — same-instance equality passes with
      `GetHashCode()` too, so only the two-instance form pins the FNV requirement.

### Task 9: `RedisLastKnownPriceStore`

- [ ] `WriteAsync` — `SET`, awaited, inside `try/catch (RedisException)`, logged and swallowed (§2.7).
- [ ] `ReadAsync` — one `MGET` for the whole missing set, inside the same catch shape. A decode failure is
      "no price", never an exception.
- [ ] Inject `IConnectionMultiplexer`; **never construct one**. A second multiplexer would win (last
      registration wins) and you would hold two connection pools, one of which the readiness probe is not
      watching.
- [ ] Tests: `Fetch_WritesLastKnownPrice`, `Fetch_RedisUnreachable_StillReturnsThePrice`,
      `Decode_CorruptValue_IsNoPriceNotAThrow`, `Encode_RoundTripsScale`.

### Task 10: `QuoteReader`, `SymbolValidator` and `MarketDataModule`

- [ ] `QuoteReader` owns the set difference (§2.5), stamps `IsLastKnown`, **and writes every quote the
      provider returned to `ILastKnownPriceStore` before computing that difference.**
      ⚠️ This is the one place the spec's wording actively misleads: §2.4 says *"Every quote this app pays an
      API call for gets written down, **from whichever path fetched it**"*, and putting the write inside
      `FinnhubQuoteProvider` satisfies the sentence while breaking the requirement — with no API key the
      **fake** is the only provider, so nothing would ever populate `marketdata:last:*` on the P0 compose
      path. That silently breaks three exit items: the `redis-cli GET` check, the kill-the-provider drill,
      and Task 19's ordering, which populates the key by hitting `/api/dashboard` on a host pinned to the
      fake. One writer in `QuoteReader` makes both paths record identically.
- [ ] `SymbolValidator` fails **open** (§2.11).
- [ ] `MarketDataModule`:

```csharp
var options = FinnhubOptions.FromConfiguration(config);   // MUST NOT throw on a missing key
services.AddSingleton(options);

if (options.HasApiKey)
{
    services.AddHttpClient<IQuoteProvider, FinnhubQuoteProvider>()
        .ConfigureHttpClient((sp, client) => { /* BaseAddress, X-Finnhub-Token, User-Agent */ })
        .AddStandardResilienceHandler(o => { /* §2.6 */ });
}
else
{
    // One owner, no cast. Registering IQuoteNudge as a cast off IQuoteProvider breaks the moment a test
    // swaps the provider out: RemoveAll<IQuoteProvider>() leaves this lambda casting the replacement.
    services.AddSingleton<FakeQuoteProvider>();
    services.AddSingleton<IQuoteProvider>(sp => sp.GetRequiredService<FakeQuoteProvider>());
    services.AddSingleton<IQuoteNudge>(sp => sp.GetRequiredService<FakeQuoteProvider>());
}

services.AddSingleton(BuildTokenBucket());                  // §3 — one bucket for the process
services.AddSingleton<ILastKnownPriceStore, RedisLastKnownPriceStore>();
services.AddScoped<IQuoteReader, QuoteReader>();
services.AddScoped<ISymbolValidator, SymbolValidator>();
```

- [ ] ⚠️ **No eager validation.** `PortfolioModule.cs:23-24` already pre-commits this in code: *"Phase 3's
      missing Finnhub key is a supported state and must not throw."* A throw here takes down `docker compose up`.
- [ ] Do **not** set `HttpClient.Timeout` — `AddStandardResilienceHandler` sets it to `InfiniteTimeSpan` and
      the pipeline owns timeouts.
- [ ] `dotnet test` — rule 4 now runs over `MarketData.Infrastructure` and must pass. If it does not, the
      resilience graph has changed; read the reported path, do not weaken the rule.

### Task 11: Deferred item C4 — extract `AddStockPortfolioRedis`

Its trigger is Phase 3, and MarketData is its first real consumer.

- [ ] New `src/Api/Extensions/RedisExtensions.cs`. **Move** from `HealthCheckExtensions.cs`: the connection-string
      name (`:19`), the blank-string throw (`:31-38`), `ConfigurationOptions.Parse` (`:40`),
      `AbortOnConnectFail = false` (`:43`), `AddSingleton<IConnectionMultiplexer>` (`:46`).
- [ ] **Reword the throw's message** — it currently names "price windows, alert cooldowns and SSE tickets",
      two of which moved to Phase 4. Phase 3's only Redis use is `marketdata:last:*`.
- [ ] `AddStockPortfolioHealthChecks` drops its now-unused `IConfiguration` parameter. An unused parameter is
      a lie the compiler will not flag; C7 can add one back when it needs one.
- [ ] `RedisHealthCheck.cs:10` already takes `IConnectionMultiplexer` from DI — **unchanged**.
- [ ] Call it immediately after `AddSingleton(TimeProvider.System)`, i.e. **before** the modules. DI is
      order-insensitive for resolution, so the ordering buys two specific things: the missing-connection-string
      throw fires before any module wiring, and "who owns the multiplexer" is answerable without reading the
      health-check file. What genuinely breaks without the extraction: MarketData injects
      `IConnectionMultiplexer` and thereby depends on `AddStockPortfolioHealthChecks` having been called —
      delete or reorder that one line and the dashboard fails on the first request, not at boot.

**Deferred item C8 is *not* triggered and stays deferred.** Its trigger reads "Phase 3, or whichever module
first adds eager validation of a runtime concern — a Finnhub key…". The second clause is not met (§2.11, and
the rule above), and the first was written when Phase 3 was expected to bring a `DbContext`. It does not
(§2.1). Doing it anyway costs three coordinated edits — split `AddIdentityPersistence` out of
`IdentityModule`, change `MigratedModules.cs`, change `ApiFixture`'s `MigratorConfiguration` — and buys
nothing this phase can demo. Phase 4's Alerts module brings a real `DbContext` **and** a `BackgroundService`;
that is where the placeholder stops being one line.

### Task 12: The dashboard read in Portfolio

- [ ] `IDashboardHoldingReader` in `Portfolio.Application/Abstractions/` — **not** a method on
      `IHoldingRepository`, whose doc comment says "Every write method here commits before it returns" and
      whose reads deliberately return **tracked** aggregates. §2.7 needs the opposite.
- [ ] Implement in `HoldingQueries` (already `internal sealed`, already the `AsNoTracking()` file):

```csharp
public Task<List<HoldingRow>> GetVisibleHoldingsAsync(Guid userId, CancellationToken ct) =>
    context.Holdings.AsNoTracking()          // `context`, not `db` — HoldingQueries.cs:9,23
      .Where(h => h.UserId == userId && h.IsVisible)
      .Select(h => new HoldingRow(
          h.Id, h.Ticker.Value, h.Quantity,
          h.AveragePrice.Amount, h.AveragePrice.Currency))   // NOT h.AveragePrice — see below
      .ToListAsync(ct);
```

- [ ] ⚠️ **Project the complex type's members, not the complex type** (§0 item 10). Rebuild `Money` after
      materialisation. `Holding.IsVisible` **does** exist (`Holding.cs:55`) and is always `true` until Phase 5
      adds the toggle, so the filter is currently a no-op that costs nothing and stops being one in Phase 5.
- [ ] ⚠️ `.Select()` after `.Include()` silently ignores the `Include`. There is no `Include` here — keep it
      that way.
- [ ] **Register it.** `PortfolioModule.cs:48` gains `services.AddScoped<IDashboardHoldingReader,
      HoldingQueries>();` beside the existing `IUserHoldsTicker` line — one class, two contracts, two
      registrations. DI here is hand-written, not convention-scanned; miss the line and `/api/dashboard`
      throws on first request, not at boot.

### Task 13: `DashboardCalculator` and the DTOs

- [ ] `DashboardPosition` / `DashboardTotals` / `GetDashboardResult` per §2.10, with `[property: JsonIgnore(Condition
      = JsonIgnoreCondition.Never)]` on **every** nullable member.
- [ ] `DashboardCalculator` — pure, no repository, no `IQuoteReader`, takes rows + prices + `now`.
- [ ] The **six** arithmetic tests from §5 (`phase-3-live-prices.md:239-244`, which already includes
      `Weight_WithNullPricePosition_ExcludesFromDenominator`) plus `Totals_CostExcludesUnpricedPositions`
      (§2.8) — **seven calculator tests**, and add `StalestObservedAt_IsMinOverPricedPositions`.
- [ ] §5's seventh entry, `Money_SerialisedAsString_NotNumber` (`:245`), is a serialisation test, not a
      calculator test. It is already satisfied for `Money` by `MoneyJsonConverter`; the assertion that matters
      is the one over **`weight` and `profitPercent`**, and it belongs with the integration tests.

The wire shape a client sees:

```json
{
  "positions": [
    { "id": "0198…", "ticker": "AAPL", "quantity": 20,
      "averagePrice": { "amount": "125.000000", "currency": "USD" },
      "cost":         { "amount": "2500.000000", "currency": "USD" },
      "currentPrice": { "amount": "150.0000", "currency": "USD" },
      "marketValue":  { "amount": "3000.0000", "currency": "USD" },
      "profit":       { "amount": "500.0000", "currency": "USD" },
      "profitPercent": "20.00", "weight": "100.00",
      "observedAt": "2026-08-05T12:00:04+00:00", "isLastKnown": false },
    { "id": "0198…", "ticker": "TSLA", "quantity": 5,
      "averagePrice": { "amount": "200.000000", "currency": "USD" },
      "cost":         { "amount": "1000.000000", "currency": "USD" },
      "currentPrice": null, "marketValue": null, "profit": null,
      "profitPercent": null, "weight": null, "observedAt": null, "isLastKnown": false }
  ],
  "totals": {
    "value":  { "amount": "3000.0000", "currency": "USD" },
    "cost":   { "amount": "2500.000000", "currency": "USD" },
    "profit": { "amount": "500.0000", "currency": "USD" },
    "profitPercent": "20.00", "positionCount": 2, "pricedPositionCount": 1
  },
  "asOf": "2026-08-05T12:00:05+00:00", "stalestObservedAt": "2026-08-05T12:00:04+00:00"
}
```

`stalestObservedAt` is `min(observedAt)` over **priced** positions (§2.8), so with one priced row it equals
that row's `observedAt` — it is `null` only when nothing is priced. TSLA is unpriced, so it contributes
nothing here and its own `observedAt` is `null` rather than a zero timestamp.

### Task 14: `GetDashboardQueryHandler` and the endpoint

- [ ] Handler returns `GetDashboardResult` **bare — no `OneOf`**. An empty portfolio is a valid dashboard and there
      is no failure case. Do not invent a union to satisfy the `.Match` convention; the rule is that a union's
      cases are exhaustive, not that every handler has one. The precedent is already in the tree and settles
      it both ways: `GetHoldingsQueryHandler` is `IQueryHandler<GetHoldingsQuery, IReadOnlyList<HoldingSummary>>`
      — bare, because listing nothing is not a failure — while `GetCurrentUserQueryHandler` is
      `IQueryHandler<GetCurrentUserQuery, OneOf<GetCurrentUserResult, NotFound>>`, because a missing user is.
      The dashboard is the first shape, not the second.
- [ ] **Zero holdings short-circuits** before touching MarketData. Without it a brand-new account pays a Redis
      round trip and an `IQuoteReader` call on every dashboard poll, forever.
- [ ] Route lives in **`Portfolio.Api`** — the dashboard is a Portfolio read that happens to need prices, and
      `CLAUDE.md` fixes the edge as Portfolio → MarketData. Putting it in `MarketData.Api` inverts that edge
      and makes MarketData read holdings, which the boundary rules forbid.
- [ ] Add a `DashboardPath` const; do **not** nest under `/api/holdings`. Reuse `TryReadUserId`,
      `.RequireAuthorization()`, `.Produces<GetDashboardResult>(200)`, `.ProducesProblem(401)`, `.ProducesProblem(500)`.
      `WithName("GetDashboard")`.
- [ ] **Register the handler.** `Portfolio.Infrastructure/DependencyInjection.cs` gains
      `services.AddScoped<IQueryHandler<GetDashboardQuery, GetDashboardResult>, GetDashboardQueryHandler>();`
      after the `GetHoldingsQuery` entry, closed generic spelled out like every other line there.
      `DecorateHandlers()` only wraps descriptors that already exist, so a missing line costs the logging
      decorator as well as the resolve.
- [ ] ⚠️ **`EndpointMetadataTests` will *not* go red, and that is the hazard.** `ShouldExposeExactly` compares
      against `EndpointsByName(routeNames)`, which filters the data source down to *the names it was handed*
      (`:157-165`, `routeNames.Contains(pair.Name, …)`). A new route called `GetDashboard` is filtered out
      before the comparison, so `EndpointDataSource_ExposesTheFourHoldingsRoutes` stays **green** and the
      dashboard route ships with **zero** metadata coverage. Nothing enumerates endpoints unfiltered.
      The edit is mandatory to *gain* coverage, not to restore green:
      add `"GetDashboard"` to `PortfolioRouteNames` (`:21-22`) — which makes
      `PortfolioRoute_ProblemStatuses_DeclareProblemJson` run over it and demand `problem+json` on the
      declared 401 and 500 — then add an `[InlineData("GetDashboard", …)]` row at `:59-68` **and** the
      matching `CallAsync` case, or `DeclaredResponses` throws `ArgumentOutOfRangeException`.

### Task 15: `AddHoldingCommandHandler` gets its lookup

- [ ] Inject `ISymbolValidator`; replace the shape-only check with `Ticker.Create` **then**
      `IsKnownSymbolAsync`. Keep returning the existing `UnknownTicker`.
- [ ] Delete the three now-stale forward-reference comments (`AddHoldingCommandHandler.cs:19`,
      `PortfolioEndpoints.cs:125`, and correct `UnknownTicker.cs:3`).
- [ ] `HoldingsTests` must stay green — the fake accepts any well-shaped symbol (§2.11).

### Task 16: Host wiring

```csharp
using StockPortfolio.Modules.MarketData.Infrastructure;
using StockPortfolio.Modules.MarketData.Api;

// block 1, immediately after AddSingleton(TimeProvider.System)
builder.Services.AddStockPortfolioRedis(builder.Configuration);      // NEW — Task 11

// block 4, after AddPortfolioApi()
builder.Services.AddMarketDataModule(builder.Configuration);
builder.Services.AddMarketDataApi();

builder.Services.DecorateHandlers();          // UNCHANGED, still after every Add<M>Module
builder.Services.AddStockPortfolioHealthChecks();   // parameter dropped — Task 11

// pipeline: NOTHING changes
app.MapPortfolioEndpoints();
app.MapMarketDataEndpoints();                 // NEW, before MapStockPortfolioHealthChecks()
```

- [ ] `appsettings.json` gains `"Finnhub": { "ApiKey": "", "BaseUrl": "https://api.finnhub.io/api/v1/" }`.
      The empty `ApiKey` **is** the documentation of the supported state. `appsettings.Development.json` is
      **not** touched — never put a real key there.
- [ ] Ordering constraints, each with what breaks: `DecorateHandlers()` after every module — Scrutor wraps
      descriptors that already exist, so a handler registered later silently loses its logging decorator.
      This does not bite Phase 3 (MarketData registers no `ICommandHandler<,>`/`IQueryHandler<,>`; the
      dashboard handler is Portfolio's) — **say that rather than implying the ordering is load-bearing here**.
      `UseCors("spa")` before `UseAuthentication()` — unchanged, Phase 3 adds no middleware.
- [ ] Do **not** add `UseRateLimiter` — the token bucket belongs inside `FinnhubQuoteProvider`; the pipeline
      version would throttle the browser rather than the outbound call. Do **not** add
      `UseResponseCompression()`.

### Task 17: The provider log line and the dev nudge

- [ ] Emit the startup line from **`MapMarketDataEndpoints`**, resolving `ILoggerFactory` off
      `IEndpointRouteBuilder.ServiceProvider`. `Map…` runs exactly once, eagerly, after `app.Build()` and
      before `RunAsync()`, with a fully built provider.
      The three alternatives each fail concretely: a factory lambda fires on first *resolution*, so with no
      dashboard request the line never appears — and §8's first checkbox is exactly "startup logs *using
      FakeQuoteProvider*"; an `IHostedService` is banned by §8 and would put `minReplicas: 0` back in play;
      and `AddMarketDataModule` has no `ILogger` to hand without building a throwaway factory.
- [ ] `Warning` for the fake ("No `Finnhub__ApiKey` configured; serving generated prices from
      FakeQuoteProvider"), `Information` for Finnhub. `[LoggerMessage]` source-generated — `CA1848` is a build
      error. Phase 6's health panel reads the same singleton, so the log string and the page string cannot drift.
- [ ] **`GET /api/marketdata/health`** — anonymous, returns `{ "provider": "<IQuoteProvider.Name>" }`,
      `.Produces<…>(200)`. It is the single source both the log line and Phase 6's panel read, so the string a
      reviewer sees in the log and the one on the page cannot drift. Add `"GetMarketDataHealth"` to the
      `EndpointMetadataTests` route-name list alongside Task 14's edit.
      This is also what makes Task 17's judgement call cheap: `MarketData.Api` now carries a type that ships
      in **every** environment, so rule 5 runs over it for real rather than only when someone runs Development
      locally.
- [ ] `POST /api/dev/nudge` maps **only** when `env.IsDevelopment()` **and** `IQuoteNudge` is registered.
      Gate on the provider, not just the environment: in Azure `ASPNETCORE_ENVIRONMENT=Production` so the
      route does not exist (404, not 401), and with a real key there is no `IQuoteNudge` to map even if
      someone deletes the environment check. `RequireAuthorization()` is explicitly **not** the gate — a
      price-manipulation endpoint any authenticated user can reach in production is still a
      price-manipulation endpoint.
- [ ] Nudges are `ConcurrentDictionary<string, (decimal Percent, DateTimeOffset ExpiresAt)>` with a TTL,
      applied multiplicatively on top of the walk, so a Phase 4 demo nudge does not persist for the session.
- [ ] The nudge binds a body, so it obeys the request convention like any other route:
      `Requests/NudgeRequest.cs` (`string Ticker, decimal Percent, int TtlSeconds`) and
      `Validators/NudgeRequestValidator.cs` beside it, wired with
      `.AddEndpointFilter<ValidationFilter<NudgeRequest>>()`, declaring `.Produces(204)`,
      `.ProducesValidationProblem()` and `.ProducesProblem(500)`. `MarketData.Api.csproj:8` already carries
      `FluentValidation` for exactly this. An `.Application` type never binds off the wire.
- [ ] The two list edits for `MarketData.Api`. Skips 4 → **2**, and the phase's architecture arithmetic is
      complete.

### Task 18: Integration-test infrastructure

- [ ] `ApiFixture.SettingsFor` gains `["Finnhub:ApiKey"] = ""` — **explicitly, not by omission.** `ApiFactory`
      appends the in-memory collection *after* the default sources, so it beats environment variables; without
      it, a developer with `Finnhub__ApiKey` exported in their shell boots the test host onto the live API and
      the suite makes rate-limited network calls while staying green.
- [ ] Give `IQuoteProvider` a `string Name { get; }` and assert it is `"Fake"`. That member is needed anyway
      for Phase 6's health panel, and it avoids asserting on a type that is `internal` in `.Infrastructure`.
- [ ] Provider-down host — a **second** `ApiFactory` against the **same containers**, following the
      `CreateHostWithClock` precedent:

```csharp
public ApiFactory CreateHostWithQuoteProvider(IQuoteProvider provider)
{
    ArgumentNullException.ThrowIfNull(provider);
    return new ApiFactory(
        SettingsFor(IdentityConnectionString, PortfolioConnectionString, _redis.GetConnectionString()),
        services =>
        {
            services.RemoveAll<IQuoteProvider>();
            services.RemoveAll<IQuoteNudge>();     // else the seam still points at the fake
            services.AddSingleton(provider);
        });
}
```

⚠️ `RemoveAll<IQuoteProvider>()` alone leaves `IQuoteNudge` registered against the fake. Today that is
masked — `ApiFactory.cs:17` runs the host as `EnvironmentName = "Testing"`, so Task 17's `IsDevelopment()`
gate never maps the route — which means the safety is accidental and the first Development-environment test
trips over it.

- [ ] Redis-down host — `CreateHostWithUnreachableDependencies()` is `static` and breaks **both** Postgres and
      Redis, so it cannot express "kill Redis with the provider up". Add an **instance** sibling:

```csharp
public ApiFactory CreateHostWithRedisDown() => new(SettingsFor(
    IdentityConnectionString, PortfolioConnectionString,
    "127.0.0.1:1,abortConnect=false,connectTimeout=500,connectRetry=1"));
```

  `abortConnect=false` stops `Connect` throwing at first resolve; `connectTimeout=500,connectRetry=1` bounds
  the wait, because the default is 5000 ms and the test would otherwise pay five seconds per call.
- [ ] Do **not** stop the `_redis` container. That mutates shared fixture state with no guarantee the stopping
      class runs last; a second host pointed at `127.0.0.1:1` is per-host and reversible.
- [ ] `Wire.UniqueTicker()` beside `Wire.UniqueEmail` — `marketdata:last:*` persists for the whole assembly
      run on the shared Redis, so `Dashboard_ProviderDown_NeverFetchedTicker_ReturnsNullNotZero` needs a
      ticker no earlier test touched. Plus `DashboardPayload` records and `GetDashboardAsync`.
- [ ] Do **not** add `ModuleDbContextInterceptors.AddToMarketData` — `SingleDbContextIn` throws when a module
      has ≠ 1 `DbContext`, and MarketData has zero.

### Task 19: Integration and unit tests

- [ ] `DashboardTests`: `…WithHoldingsAndPrices_ReturnsJoinedTotals` · `…NewlyAddedTicker_HasPriceOnFirstRequest` ·
      `…ProviderDown_ShowsLastKnownWithAge` · `…ProviderDown_NeverFetchedTicker_ReturnsNullNotZero` ·
      `…ProviderReturns429_Returns200NotError` · **`…PartialProviderFailure_MixesFreshAndLastKnown`** (new, §2.5) ·
      `…RedisDown_StillReturnsFreshPrices` · `…OnlyReturnsCallersHoldings` · `…GeneratedSql_UsesParameterPlaceholder`.
- [ ] Ordering for `…ProviderDown_ShowsLastKnownWithAge`: both hosts share `_redis`, so populate the key by
      adding a holding and hitting `/api/dashboard` once on the **shared** host, then boot the dead-provider
      host and hit it again. Two hosts, one Redis.
- [ ] `…RedisDown_StillReturnsFreshPrices` asserts the pair: `/api/dashboard` → **200 with fresh prices** and
      `/health/ready` → **503**. Degraded, not broken.
- [ ] `Dashboard_GeneratedSql_UsesParameterPlaceholder` needs **no** fixture change —
      `ModuleDbContextInterceptors.AddToPortfolio` is already wired — but it must run against
      `_fixture.CreateClient()`, since `RecordedCommands` is passed only to the shared host.

### Task 20: The SPA dashboard

- [ ] `src/marketdata/dashboardApi.ts` — `GetDashboardResult` types, `dashboardKeys.view()`, `fetchDashboard` using
      `apiFetch` with the `signal`.
- [ ] `src/lib/format.ts` — `formatMoney(money)` passing the **string** straight to
      `Intl.NumberFormat(undefined, { style: 'currency', currency }).format(money.amount)`, and
      `formatPercent(s)` appending a literal `%`. Branch on null **before** the formatter. `portfolio.tsx:86-91`
      currently does `Number(money.amount).toLocaleString(...)` — **rewrite it, do not copy it** (§0 item 13).
- [ ] Rewrite `routes/_authenticated/dashboard.tsx`: **no `loader`, no `errorComponent`** — unlike
      `portfolio.tsx`, whose own comment at `:19-30` already says Phase 3's quotes must not get one. Use
      `useQuery` (the app has only ever used `useSuspenseQuery`; this is the first `useQuery`), overriding the
      global defaults, which are `staleTime: 30_000` and `refetchOnWindowFocus: false`:

```ts
const [intervalMs, setIntervalMs] = useState(60_000)   // 60s default, per spec §3

useQuery({
  queryKey: dashboardKeys.view(),
  queryFn: ({ signal }) => fetchDashboard(signal),
  refetchInterval: intervalMs,        // runtime-configurable local state
  refetchOnWindowFocus: true,
  staleTime: 0,
})
```

⚠️ **The 60s default is not arbitrary and is not free to change.** §3's free-tier arithmetic — twenty of
sixty calls per minute for one viewer — assumes it, and so does the README paragraph Task 23 schedules.
Picking 15s quadruples that figure. `<select>` options: 15s / 30s / 60s / 5m.

- [ ] Extract the four hardcoded tiles at `dashboard.tsx:21-32` into `StatTile`. Reuse `Table` and
      `Column<T>` as-is for the eight columns (`numeric: true` gives right-align + `font-mono`). Reuse `Card`'s
      `action` slot for the freshness line and interval `<select>`.
- [ ] `index.css` needs a **`--warn` token** in `:root`, `.dark` and `@theme inline` — there is only
      `--up`/`--dn` today, and `Alert` has no `warning` tone.
- [ ] Delete the stale "Phase 2" captions and the placeholder paragraph (§0 item 12).
- [ ] `tests/dashboard.test.tsx`, copying `portfolio.test.tsx`'s boilerplate: clear the module-singleton
      `queryClient` in `beforeEach`, `router as AnyRouter`, scope row queries through
      `within(screen.getByRole('table'))` because the mobile list duplicates text, and assert money by **digits
      only** — the runner locale is en-GB and renders USD as `US$120.00`. Five tests: totals render without
      client-side arithmetic · a null price renders pending, not `$0.00` · a stale timestamp shows amber ·
      changing the interval control changes `refetchInterval` · a provider error keeps the last good table.
- [ ] **`src/components/ApiHealth.tsx`** — the stub panel the spec asks for ("stubbed here, filled in Phase
      6"). Reads `GET /api/marketdata/health` and renders the provider name, with a Phase 6 placeholder for
      latency and quota. Without it, the exit item "the health panel names Finnhub rather than Fake" is
      unreachable.
- [ ] ⚠️ **Two existing Phase 1 tests will go red, and neither is a dashboard test.**
      `tests/setup.ts:13` runs MSW with `onUnhandledRequest: 'error'` and `tests/msw/server.ts:8` registers no
      default handlers — while `auth.test.tsx:131` (`renderAt('/dashboard')`) and
      `sessionPersistence.test.tsx:55` (`initialEntries: ['/dashboard']`) both mount this route. The moment
      the route fetches, both fail on an unhandled request. Add a shared `dashboardHandler` to `tests/msw/`
      and `server.use(...)` it in both files.
- [ ] **`portfolio.tsx` is edited too, and this is a `CLAUDE.md` breach being carried, not a new one.**
      `:86-91`'s `Number(money.amount).toLocaleString(...)` is deleted in favour of `formatMoney` from
      `src/lib/format.ts`. `:102-109`'s `totalInvested` is a `Number()` **reduce over money in the browser** —
      *"Never compute money in the browser"* — and Phase 3 is the phase that makes a server-computed total
      available. Either replace it or defer it **with the reason recorded here**; leaving it unmentioned reads
      as an oversight and the next reader fixes it at random.
- [ ] `npm install` first — `node_modules` is absent.

### Task 21: Compose, and the degradation drills

- [ ] `docker compose up` from a clean clone with **no** `FINNHUB_API_KEY` → the warning line, then a priced
      dashboard.
- [ ] Optional but worth it for the drills: deferred item **D10** — `redis: condition: service_healthy` in
      `docker-compose.yml:118-119`. Do **not** append `,abortConnect=false` to the compose connection string;
      `RedisExtensions` sets it imperatively for every environment, and two places to change it is how they
      diverge.
- [ ] Run the four §8 drills by hand: provider down · provider down + Redis flushed · Redis down with the
      provider up · `redis-cli GET marketdata:last:AAPL`.

### Task 22: Deploy — ❌ **NOT DONE, and "no `az` CLI" is not the reason.**

> ⚠️ **This task was first recorded as blocked on the missing Azure CLI. That was wrong, and the correction
> came from a document nothing linked to** —
> [docs/superpowers/specs/2026-08-02-azure-deployment-design.md](../superpowers/specs/2026-08-02-azure-deployment-design.md),
> whose only inbound reference was inside itself. Two facts it carries change this task:
>
> 1. **There has been a live, healthy Azure deployment since 2026-08-02.** Re-verified 2026-08-05:
>    `/health/ready` → 200, SPA → 200. It serves **pre-Phase-3** code — `/api/dashboard` and
>    `/api/marketdata/health` both 404.
> 2. **`deploy.yml` installs Bicep and runs `az deployment group what-if` in the runner**, and fires on push
>    to `main` or `workflow_dispatch`. A local `az` only buys the rehearsal.
>
> So what is actually outstanding is a **merge to `main`** and the **`FINNHUB_API_KEY` secret** — not a tool
> install. The local rehearsal stays genuinely blocked, and with it the one cheap thing §2.9 wanted: whether
> the `@secure()` module output trips the `outputs-should-not-contain-secrets` linter class. `bicep build`
> has never run locally in any phase, though the deployed stack proves the template compiles and applies.
> `infra/` was not touched this phase, so "what-if reports no changes" is a good prediction — still a
> prediction.

- [ ] `az bicep build --file infra/main.bicep` (§2.9) — **not run locally, and genuinely blocked**: no `az`
      here. This is the rehearsal, and it is the only part of Task 22 the missing CLI actually stops.
- [ ] `az deployment group what-if` → expect **no changes**. **Not run locally.** Note the workflow runs it
      too, so merging to `main` executes this check even if the local rehearsal never happens — it just
      executes it a moment before the deploy rather than a day before.
- [ ] Set the real `FINNHUB_API_KEY` repository secret, deploy, and confirm the deployed dashboard shows
      genuine prices and the health panel names Finnhub rather than Fake. **Not done — the secret is not set,
      so the deployed app would price real tickers from the generated walk.** This also blocks the exit item
      "adding a genuinely non-existent symbol with a real key returns `UnknownTicker`": `FakeQuoteProvider`
      accepts any well-shaped ticker by design (§2.11), so the only host that can produce `UnknownTicker` is
      one holding a real key, and `finnhub.io` was unreachable from here besides. The response mapping is
      unit-tested and `IsKnownSymbolAsync` can now return `false`; the end-to-end path is unexercised.

### Task 23: Correct the documents Phase 3 disproved — ✅ **DONE, with three items outstanding**

- [x] `CLAUDE.md`: the `minReplicas` line (now Phase 4, with the cold-start cost stated) · the module-count
      self-contradiction (four designed, three on disk, `ShouldBe(17)` as the pin) · the project count
      (23 → **25**) · the Tests table and skip arithmetic **re-derived from `ModuleBoundaryTests.cs`, not
      copied** · the "one DbContext per module" exception (§2.1) · the connection-budget number (→ **8**).
      Three additions the checklist did not ask for: the `/quote`-cannot-discriminate trap, the
      resilience-defaults trap, and the discriminating-test lesson from §2.5 beside the existing
      "a test that cannot fail" rule.
- [x] `docs/plan/00-overview.md`: the solution-layout block (Portfolio no longer "holdings **and alerts**";
      MarketData annotated as having no `DbContext`; Alerts listed as Phase 4 with nothing on disk) and the
      connection budget at `:149`.
      ⚠️ **The section was *not* renamed back, and the link count was wrong.** Renaming `## Four modules` to
      "Three modules, not four" would resolve the anchors by making the heading contradict the decision it
      documents, which is the failure this whole task exists to prevent. All references were repointed
      instead — and there are **ten of them across eight files**, not nine: `phase-1-implementation.md` ×2,
      `phase-1-sign-in.md`, `phase-2-implementation.md` ×2, `phase-2-my-portfolio.md`, `phase-4-alerts.md`,
      `phase-5-make-it-mine.md`, `phase-6-doesnt-break.md`, `deferred-work.md`. Three of those carried a
      substantive claim as well as a dead anchor and were corrected in place: `phase-5-make-it-mine.md`
      (the alert threshold is `alerts`' again, not `portfolio`'s), `phase-4-alerts.md` (the cooldown clear is
      not an in-process call any more — and that still does not bring the domain event back), and
      `phase-1-implementation.md` (project count).
- [x] `docs/plan/module-interactions.md`: `minReplicas: 1` in the diagram **and** at `:225`, plus the
      "3 roles × pool size 2" edge label, which had the same miscount as the budget figures.
- [x] `docs/plan/er-diagram.md`: the `23505` line · the stale §2.4 cross-reference · and the connection budget
      at `:153`, which the checklist did not list but which was wrong in the same way as the other three.
- [x] `docs/plan/phase-3-live-prices.md`: a §0 correction header plus eleven in-place markers, in
      `phase-2-implementation.md` §0.0's style — the original text struck through and left visible, never
      rewritten to the new shape.
- [x] `docs/deferred-work.md`: **E1 reopened** with the reason (Alerts was reinstated as a decision, not as
      code) and a new, checkable closing condition · **C4 marked done**, including the two things doing it
      revealed · **C8 recorded as examined-and-still-deferred**, with both clauses of its trigger shown to
      have failed and the trigger restated for Phase 4 · **C7 noted as considered and not applicable**,
      because MarketData has no database and contributes no `AddDbContextCheck<T>()`. Also corrected the
      "Checked and found correct" entry on `Maximum Pool Size=2`, whose arithmetic was wrong.
- [x] `README.md`: the "empty shells" line · the subdomain-classification line · the connection-budget
      arithmetic · the status banner (Phase 1 → Phase 3) · the Testing table · and a new **Live prices**
      section carrying all four required topics, with the 60/minute free-tier figure stated as **inferred**
      rather than laundered into fact.
- [x] `src/Api/Extensions/HealthCheckExtensions.cs`'s comment — **already done in Task 11.** The text moved to
      `RedisExtensions.cs` and was reworded to name `marketdata:last:*` rather than price windows, alert
      cooldowns and SSE tickets. Nothing left to do.

⚠️ **Outstanding — two stale comments in files the documentation pass was scoped out of.** Both are one-line
comments, neither affects behaviour, and each is a claim the tree now contradicts. The decision each one needs
is made and recorded here, so applying them is a mechanical edit:

| File | Comment | What it should say |
|---|---|---|
| `docker-compose.yml:65` | `# Redis - price windows, alert cooldowns, SSE tickets (phases 3-4).` | Phase 3's only Redis use is `marketdata:last:*` — the last known price of every symbol, and the dashboard's only fallback. The other three moved to Phase 4. Match the wording `RedisExtensions.cs` now carries |
| `src/Web/src/routes/_authenticated/portfolio.tsx:297` | `{/* No price and no P&L columns — those need MarketData, which is Phase 3. */}` | **Decided: the dashboard owns them, and the comment should say so.** Not "Phase 3 has not got to it" — adding price columns here would make a CRUD screen pay the provider fan-out on every render, for a page that exists to edit positions. Recorded in the README's Known gaps so the decision survives the comment |

The third comment this task listed, `portfolio.tsx`'s `totalInvested` `Number()` reduce, **needs no edit**: the
implementer already replaced the bare code with a doc comment that names it as the `CLAUDE.md`
"never compute money in the browser" breach, defers it explicitly, and gives the reason — reaching
`GetDashboardResult.totals.cost` means calling `/api/dashboard`, which spends one provider call per position
against the free tier's budget from a page that shows no prices. The real fix it names, a cost total on
`GET /api/holdings`' own response, is unscheduled and should be picked up by whichever phase next touches that
endpoint.

### Task 24: The phase is done when it runs, not when tests pass

Work the §8 checklist in a browser, on compose and then on the deployed URL.

---

## 6. Work order

| # | Task | Verified by |
|---|---|---|
| 1 | Packages, `MarketData.UnitTests` | builds, discovered, 0 tests; project count 25 |
| 2 | `MarketData.Domain` | 3 tests; **architecture suite goes red here** |
| 3 | Architecture lists — `.Domain` only | skips 11 → **9**; suite green; one rule broken on purpose |
| 4 | `MarketData.Contracts` (+ its list edit) | rule 2 runs over two modules; skips 9 → **7** |
| 5 | `Portfolio.Application` → `MarketData.Contracts` | rule 1 still green |
| 6 | `LastKnownPrice` | 3 tests, each able to go red |
| 6b | `Application/Abstractions` — the three ports (+ list edit) | builds; skips 7 → **6** |
| 7 | `FinnhubQuoteProvider`, `InternalsVisibleTo` (+ list edit) | 6 mapping tests; rule 4 runs; skips 6 → **4** |
| 8 | `FakeQuoteProvider` | determinism **across two instances** |
| 9 | `RedisLastKnownPriceStore` | 4 tests incl. the unreachable-Redis one |
| 10 | `QuoteReader` (owns the Redis write), `SymbolValidator`, `MarketDataModule` | one shared token bucket, one writer |
| — | *half day* | |
| 11 | C4 — `AddStockPortfolioRedis` | `/health/ready` still 503 with Redis down |
| 12 | The dashboard read **+ its DI line** | generated SQL read by eye: no `Include`, members projected |
| 13 | `DashboardCalculator` + `GetDashboardResult` | **7 calculator tests**; `weight`/`profitPercent` are strings |
| 14 | Handler + endpoint **+ its DI line** | `/openapi/v1.json` names `GetDashboardResult`; metadata coverage **gained**, not restored |
| 15 | `AddHoldingCommandHandler` lookup | `HoldingsTests` still green |
| 16 | Host wiring, `appsettings.json` | manual run: dashboard returns 200 with prices |
| 17 | Log line, health route, nudge (+ last list edit) | keyless boot logs the warning; rule 5 runs; skips 4 → **2** |
| 18 | Fixture: three host shapes | `dotnet test` green |
| 19 | Integration tests | 9 tests incl. the partial-failure one |
| — | *one day* | |
| 20 | SPA dashboard, health panel, the two MSW fixes | **works in a browser**; cards at 375px |
| 21 | Compose + the four drills | all four by hand |
| 22 | Bicep build, what-if, deploy | what-if reports **no changes** |
| 23 | Document corrections | — |
| 24 | The §8 walkthrough | on the public URL |
| — | *0.8 days total* | |

Task 2 is the moment the architecture rules switch on. **The list edits are then spread across tasks 3, 4,
6b, 7 and 17 — one assembly each — because making both edits at task 3 trades one red test for two, held
red for fourteen tasks.** Skips fall 11 → 9 → 7 → 6 → 4 → 2, and the suite is green after every task.

---

## 7. Risks and deviations, stated up front

**This plan was written in an environment that cannot build it.** No `dotnet` SDK, no Docker daemon, no `az`,
and `src/Web/node_modules` absent. Every count in it is derived from source by inspection — the skip
arithmetic in Task 3 from the five `SkipIfEmptyShell` call sites, the test counts from `[Fact]`/`[InlineData]`/
`MemberData` arity — and **not one of them was measured by a run**. The first thing the executor should do is
measure, and treat a disagreement as this plan being wrong.

**`finnhub.io` is unreachable from here too** (the agent proxy denies CONNECT to it, and to
`learn.microsoft.com`). The Finnhub facts in §2.6 come from their vendored OpenAPI spec, their official Python
client and their issue tracker — good sources, but not their documentation. The two claims that matter most
are the two that are weakest: the 60/minute free-tier figure is snippet-only, and the all-zero semantics are
contradicted rather than merely unconfirmed. **§2.6 marks each one; do not launder them into fact when
writing the README.**

**Microsoft's Agent Skills contributed structure, not content.** All seven Azure skills consulted
(`azure-managed-redis`, `azure-container-apps`, `azure-cache-redis`, `azure-resiliency`, `azure-key-vault`,
`azure-monitor`, `azure-well-architected`) are URL indexes generated by `docs2skills/1.0.0` that assert
nothing about ports, SKUs, secrets or probes — every one carries *"Requires network access"*, and that access
is blocked. They named the right pages; the technical claims in §2.9 come from the repo's own Bicep comments
and from web search, and are attributed that way. `azure-resiliency` in particular is the **wrong** skill for
this: its scope is Backup and Site Recovery vaults, not application-level fallback. Cite
`azure-well-architected`'s transient-fault and health-modelling guidance instead — and note the one genuine
tension, that WAF's performance guidance is cache-first while this design is deliberately provider-first. The
justification is correctness, not performance: **a cached stock price is a wrong stock price.**

**Alerts is a module in the documents and nowhere on disk.** `CLAUDE.md`, `00-overview.md`,
`module-boundaries.md`, `module-interactions.md`, `er-diagram.md` and `phase-4-alerts.md` all describe four
modules; `src/Modules/` has three and `ModuleBoundaryTests.cs:17` pins seventeen assemblies. Phase 3 does not
build it and does not clean up the `alerts` schema, role or deployment variables — `docker compose up` is the
P0 gate, `db/init/` has already broken it once, and this environment has no Docker daemon to re-verify with.
That is the same trade `deferred-work.md` E1 made, and E1's "RESOLVED" is reopened rather than acted on.

**Phase 3 puts an outbound HTTP call on a write path** (§2.11). It fails open, and that is the whole
mitigation. If a Finnhub outage is ever observed to block adds anyway, the bug is in the fail-open path, not
in the design.

**The free tier is the real constraint, and it does not scale.** Twenty positions is twenty of sixty calls per
minute for one viewer. Three viewers exhaust the budget. This is fine for a demo and is a genuine property to
state in the README rather than an oversight to hide.

**`Money`'s constructor still runs `ToUpperInvariant()` on every materialised row**, and Phase 2's own risk
section said Phase 3's dashboard join is where that becomes worth re-measuring. This plan does not fix it —
the fix is moving normalisation into the factories, which edits a Phase 1 file with a passing suite. Measure
first; if a twenty-row dashboard shows nothing, leave it and delete this paragraph rather than carrying it to
Phase 4.

**`MarketData.Api` was nearly a dev-only assembly, and that would have been the weakest thing in this plan.**
An endpoint that exists only in Development means rule 5 runs over `MarketData.Api` only when someone happens
to run Development locally — enforcement in name. The health route (Task 17) fixes it by accident of being
useful: it ships in every environment, so the assembly carries a real type and rule 5 genuinely runs. The
nudge is still a state-mutating route excluded from Production and is still gated twice (§2.11) — but it is
no longer load-bearing for the architecture argument, which is why it is safe to drop if it looks wrong on
the day.

⚠️ **If anyone writes deferred item C11's test during this phase**, it must read "every **non-shell** `.Api`
exposes `Map<M>Endpoints`" — reusing `SkipIfEmptyShell`. Written naively it goes red on a deliberately empty
`MarketData.Api`, and the fix then *looks* like "add an endpoint" instead of "fix the rule".

---

## 8. Phase 3 exit checklist

- [x] `docker compose up` from clean volumes with **no** `Finnhub__ApiKey` → the log says *"No
      Finnhub__ApiKey configured; serving generated prices from the Fake quote provider"*, and the dashboard
      shows prices
- [x] Add a brand-new ticker → priced on the **very first request**, no pending state. Verified with NVDA
      added seconds before the read
- [x] KPI tiles and the totals row agree with the per-row numbers — $1,708.52 + $1,587.22 + $4,203.83 =
      **$7,499.57**, against invested $5,980.00 and P&L $1,519.57
- [x] Weights sum to 100% within `pricedCount × 0.005` — 22.78 + 21.16 + 56.05 = **99.99**, tolerance 0.015
- [x] **Kill the provider and refresh** → every position still listed, `isLastKnown: true`, the original
      `observedAt` preserved so the age renders, **200**, no blank table. Exercised against a second API
      container carrying a real-shaped key pointed at an unreachable base URL, so `FinnhubQuoteProvider`'s
      own failure path ran rather than a simulation of it. It cost **5.8s** — the resilience pipeline
      spending its retries and timeouts, which stacks on cold start while `minReplicas` is 0
- [x] Kill the provider **and** flush Redis → positions still list; `currentPrice`, `marketValue`, `profit`,
      `profitPercent`, `weight` and `observedAt` all `null` **and present in the JSON**;
      `pricedPositionCount: 0`; still **200**
- [x] Kill **Redis** with the provider up → prices fresh and `isLastKnown: false` on every row, and
      `/health/ready` returns **503**
- [x] Fail **some** tickers, not all → fresh and last-known mixed in one 200 response. Covered by
      `Dashboard_PartialProviderFailure_MixesFreshAndLastKnown` rather than by a hand drill: the compose path
      runs the fake, which has no per-ticker failure mode. §2.5's ⚠️ is why this test exists — and
      `Dashboard_ProviderReturns429_Returns200NotError` was found to pass under the rejected implementation
      and had to be strengthened to assert `IsLastKnown == false` on served symbols
- [x] `redis-cli GET marketdata:last:AAPL` returns `"{price}:{epochMs}"` after one dashboard load —
      measured `210.3684:1785956606311`
- [ ] ❌ **UNVERIFIED.** Adding a genuinely non-existent symbol with a **real key** returns `UnknownTicker`;
      adding a valid one with the provider **down** still succeeds. The second half is covered
      (`SymbolValidator` fails open, and there is a test). The first half cannot be exercised here: no
      `FINNHUB_API_KEY` is set, `finnhub.io` was unreachable, and `FakeQuoteProvider` accepts any well-shaped
      ticker by design (§2.11) so it can never produce the case. What *is* covered is the `/search` response
      mapping, unit-tested, and `IsKnownSymbolAsync` returning `false` on a non-matching result — which is
      more than the old `/quote` design could ever have been tested for, since its "unknown" and its "provider
      blipped" were the same response
- [x] No `BackgroundService`, `PeriodicTimer` or `IHostedService` anywhere in `src/` — grep clean
- [x] `dotnet test` green — passing **and** skipped both quoted, and the skip count is **2** against a freshly
      measured baseline (the 11 this plan quotes is derived, not measured). It was green after **every** task
      along the way, not only at the end.
      **Measured: 416 passed, 2 skipped, of 418 discovered**, at 0 build warnings — `Shared.Kernel` 21,
      `Identity` 98, `Portfolio` 90, `MarketData` 61, `Architecture` 46 + **2 skipped**, `Api.IntegrationTests`
      100. The skip arithmetic in Task 3 predicted 2 and measured 2. Both remaining skips are
      `Identity.Contracts` — rules 1 and 2 — and both are correct: nothing reaches into Identity, so its
      `.Contracts` is deliberately empty. The **shell list is now one entry**, which is a different quantity
      from the skip count and must not be read off it
- [x] `GET /api/marketdata/health` names the active provider, and the SPA panel shows the same string —
      `{"provider":"Fake"}` anonymous, and the browser panel reads **Fake**. One source feeds the log line
      and the page, so they cannot drift
- [x] `stalestObservedAt` is populated on a dashboard with a last-known row, and `null` when nothing is
      priced — both halves seen in the drills
- [x] One architecture rule broken on purpose and seen red, then restored — rule 1 reported
      `MarketData.Domain -> Portfolio.Domain` with the `<ProjectReference>` **plus** a fully-qualified field,
      because a `using` alone is a compile error and a bare `Ticker` binds locally and reports green
- [x] `/openapi/v1.json` names `GetDashboardResult`; no `.Application` request type appears in it — read off
      a running host, not the source
- [x] `weight` and `profitPercent` are JSON **strings** in a real response, and a null price is `null` rather
      than absent. Stripping `JsonIgnore(Condition = Never)` left every *deserialised* assertion passing —
      only the raw-JSON check catches it, because a deserialiser cannot tell absent from null.
      `DashboardTotals.ProfitPercent` was later made `string?` too: `"0.00"` on a dashboard where nothing is
      priced claims break-even at the moment nothing is known, which is the same argument that already made
      `Weight` null rather than zero
- [x] `npm test` green — **26 passing across 6 files**, including "a provider error keeps the last good table
      on screen" and the two Phase 1 session tests that mount `/dashboard` under MSW's
      `onUnhandledRequest: 'error'`
- [ ] ❌ **NOT RUN LOCALLY — no `az` CLI here, and this is the only part of Task 22 that blocks on it.**
      `az bicep build` clean, and `az deployment group what-if` reports **no changes**. Zero lines under
      `infra/` were touched, so no changes is the expectation. `deploy.yml` runs `what-if` in the runner, so
      merging to `main` executes it regardless. See Task 22
- [ ] ❌ **NOT DONE — needs a merge to `main` and the secret, not a tool install.** Deployed **with a real
      `FINNHUB_API_KEY`**; the public dashboard shows genuine prices and the health panel names Finnhub rather
      than Fake. A live deployment exists and is healthy, but serves pre-Phase-3 code. See Task 22
- [x] Table → cards at 375px, totals still legible — table hidden, card list carrying the same rows, and
      `documentElement.scrollWidth === innerWidth`, so no horizontal overflow
- [x] README: why the dashboard asks the provider directly rather than reading a cache · the last-known-price
      fallback and the staleness call from §2.6 · why `dp` is not used for thresholds · the free tier's
      60-calls-per-minute ceiling and what it means for concurrent viewers. **Provenance is stated rather than
      laundered**: the 30/second burst cap is marked confirmed, the 60/minute free-tier figure is marked
      inferred from a search snippet because `finnhub.io` was unreachable, and the all-zero semantics are
      described by what shipped rather than by the spec's contradicted claim
- [x] The counts and claims Task 23 lists are corrected in `CLAUDE.md`, `README.md`, `docs/deferred-work.md`
      and **seven** `docs/plan/` files — the four named plus the three that carried a dangling anchor together
      with a substantive claim. The two remaining comments — `docker-compose.yml`'s Redis header and
      `portfolio.tsx`'s price-columns note — are now corrected as well
