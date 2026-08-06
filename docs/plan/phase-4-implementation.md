# Phase 4 — Implementation plan

Companion to [phase-4-alerts.md](phase-4-alerts.md). That file says *what* Phase 4 must do and which traps
to avoid. This one says *which files exist, in which project, referencing what, built in which order* — the
same relationship the phase-1, -2 and -3 implementation plans had to their phase files. It is deleted when
Phase 4 ships.

**Goal:** set a threshold on a position, click **Simulate**, and see the alert in the panel in under a
second. Then nudge a price past a threshold in the local stack and see the same row arrive from a real
evaluation, with no refresh, and with the connection still alive five minutes later.

**Architecture:** Alerts becomes the fourth module, with the same five layers. MarketData gains the only
background job in the application — a poller that samples the tickers somebody has an alert on and keeps a
trimmed series per ticker in Redis. MarketData still depends on nothing: it declares **two** ports, one
inbound (*which tickers do I poll*) and one outbound (*a fresh sample landed*), and the host adapts both to
`Alerts.Contracts`.

**Tech stack:** no new NuGet package on the server beyond what Alerts' persistence needs
(`Microsoft.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL` — both already in
`Directory.Packages.props`). No new npm package. Bicep changes for the first time since Phase 1.

---

## Global constraints

Copied from root `CLAUDE.md`; every task below inherits them.

- **`.Infrastructure` never references ASP.NET Core. `.Api` never references EF Core or its own
  `.Infrastructure`.** They meet only through `.Application/Abstractions`.
- **`.Infrastructure` is `internal`** except the `AlertsModule` class. `.Domain` and `.Application` are
  `public`. `.Contracts` is `public` and holds **records of primitives only** — raw `Guid`, `string`,
  `decimal`. No EF reference, no strongly-typed ids.
- **A module references only other modules' `.Contracts`.** Only `Architecture.Tests` enforces this.
- **Every CQRS type** lives at `Application/<FeatureArea>/{Commands,Queries}/<UseCase>/`, with the role in
  the class name — `SaveAlertSettingCommand`, `SaveAlertSettingCommandHandler`. The namespace stops at the
  use-case folder and does **not** repeat the `Command` suffix.
- **Requests live in `.Api`**, never `.Application`. `<UseCase>Request.cs` plus `<UseCase>RequestValidator`,
  and `ValidationFilter<T>` closes over the **request** type.
- **A handler returns `OneOf<…>` directly.** No `[GenerateOneOf]`, no named union class. Map with `.Match`,
  never a `switch` over `.Value`. **Name every `.Match` lambda parameter.**
- **An entity has exactly one constructor**: private, all mapped values, assigning and nothing else, **no
  validation** — EF binds it by parameter name on every row of every `SELECT`. Validation lives in the
  static `Create` returning a `OneOf`.
- **Endpoint handlers return `Task<IResult>`**, and every endpoint declares every status it can emit.
  Verify against `/openapi/v1.json`, never against the source.
- **Money is `decimal` server-side, serialised as strings.** Percentages are computed server-side too.
- **EF Core only, no raw SQL.**
- **Never add `UseResponseCompression()`.** It buffers `text/event-stream` and the feed dies silently.
- **Comments: one line**, `/// <summary>…</summary>` only.
- Frontend: **no external UI component library**. Hand-built on Tailwind, native controls.

---

## 0. Read this first — where `phase-4-alerts.md` under-specifies

Nothing in the phase file is *wrong*. Six things it leaves open would each be decided differently by two
engineers, and three of them are decisions that cannot be unmade cheaply. All six are settled in §2.

| # | The phase file says | What it does not say | Settled in |
|---|---|---|---|
| 1 | "MarketData declares that it needs a list of tickers to poll, and the host supplies the adapter" | It also says evaluation runs "immediately after each fetch, in the same cycle" — which means the poller must *call* Alerts. A second, outbound port is needed, or MarketData ends up depending on Alerts | §2.1 |
| 2 | "Three workable constraints, and this phase has to pick one" | Which one | §2.2 |
| 3 | "Both ends must be in the same trading session" | How, with no market calendar anywhere in the codebase | §2.3 |
| 4 | "A stale feed suppresses price alerts entirely and raises a feed-health signal instead" | What a feed-health signal *is* | §2.4 |
| 5 | "Retention is checked against the maximum configurable window at startup" | Retention belongs to MarketData and the cap belongs to Alerts, so nothing inside either module can see both | §2.5 |
| 6 | "Move it one assembly at a time so the suite is green after each step" | Which assembly first, and what the empty-shell list becomes | §2.6 |

Two claims in neighbouring documents are stale and are corrected by this phase rather than argued with:

- **`infra/main.bicep` passes `minReplicas: 0`**, overriding the module default of 1. Task 10 changes it, and
  the exit condition in `phase-4-alerts.md` §8 — no `BackgroundService` anywhere in `src/` — inverts with it.
- **`deferred-work.md` E1** says the `alerts` schema, the `alerts_svc` role and the deployment variables are
  unowned. Task 4 makes them owned. Do not tick E1 off on the strength of this plan; tick it off when
  `AlertsDbContext` has connected as `alerts_svc` against a real database.

---

## 1. Scope

Brief **P1 req 9** in full, plus the deferred items whose written trigger is this phase.

Phase 4 **does** build: the Alerts module end to end; the quote poller and the trimmed price window; alert
evaluation with the sign-agreement rule, the three guards and the cooldown; the ticket handshake and the
server-sent-events stream with cross-replica fan-out; Simulate; the dashboard panel and the notifications
screen; and the infrastructure changes that a background job forces.

Phase 4 also closes three items from [deferred-work.md](../deferred-work.md), each of which names this phase
in writing:

| Item | What closes it | Task |
|---|---|---|
| **C11** — a fourth module that is never mapped passes every test | `EndpointMetadataTests` derives its module set from the loaded `StockPortfolio.Modules.*.Api` assemblies instead of a literal list | 2 |
| **E1** — `alerts` schema, `alerts_svc` role and deployment variables with no module behind them | `AlertsDbContext` connects as `alerts_svc` | 4 |
| **C8** — the Migrator invents a JWT signing key | `AddIdentityPersistence` splits out of `AddIdentityModule` | 5 |

Phase 4 does **not** build: alert replay or backfill of any kind, a watchlist, per-user provider keys,
theme or language, the degradation UI (Phase 6), or **C6** and **B4/B6** from the deferred register. Those
two stay deferred and their status lines are updated in Task 16 rather than left to rot.

**Counts that change.** Projects 26 → **32** (five Alerts projects plus `Alerts.UnitTests`). Architecture
assemblies 17 → **22**. Registered `DbContext`s 2 → **3**, so the connection ceiling goes 8 → **12** of the
tier's 35. Architecture skips go 2 → 9 → 7 → back to 2 as each Alerts layer fills — see §2.6.

---

## 2. Decisions settled before any code

### 2.1 DECISION — MarketData declares two ports, and the host adapts both

The phase file gives MarketData one port, *which tickers do I poll*. That is not enough. Evaluation must run
"immediately after each fetch, in the same cycle", and evaluation belongs to Alerts. Written naively, the
poller in MarketData calls Alerts — the one edge the dependency graph forbids, and the one
`ModuleBoundaryTests` would catch only after the code was written.

So MarketData declares **two** abstractions in `.Application/Abstractions/`, and the host supplies an
adapter for each:

```csharp
// src/Modules/MarketData/…Application/Abstractions/IPollTargetSource.cs
/// <summary>The tickers worth sampling this cycle; an empty list is the ordinary case and means no work.</summary>
public interface IPollTargetSource
{
    Task<IReadOnlyList<string>> GetPollTargetsAsync(CancellationToken ct);
}

// src/Modules/MarketData/…Application/Abstractions/IPriceSampleObserver.cs
/// <summary>Told once per ticker per cycle, after the sample is stored. Must never throw: a failed
/// observer must not stop the next ticker being sampled.</summary>
public interface IPriceSampleObserver
{
    Task OnSampleStoredAsync(string ticker, CancellationToken ct);
}
```

Both are worded as MarketData's own need, not as "ask Alerts". If the answer ever comes from somewhere else,
only the host adapter changes. The default registration for `IPriceSampleObserver` is a no-op, so MarketData
runs standalone in its own unit tests without a stub.

Why not put the poller in the host, where it could see both modules? Because it needs the token bucket, the
`IQuoteProvider` registration and the window store, all of which are `internal` to
`MarketData.Infrastructure`. Why not put it in Alerts and have Alerts pull prices? Because that makes Alerts
own the rate limit, the resilience policy and the last-known-price write — three things `CLAUDE.md` names as
MarketData's, and the last of which must have exactly one writer.

### 2.2 DECISION — the false-positive constraint is **sign agreement**

Both measurements of the window must point the same way before anything fires.

- **Endpoint move** = `(current − oldest) / oldest`.
- **Extreme move** = `(current − high) / high` when falling, `(current − low) / low` when rising.

An alert fires only when `sign(endpointMove) == sign(extremeMove)` and `|extremeMove| ≥ threshold`. The
extreme move is what is reported, because it is the larger and the one the user cares about; the endpoint
move is carried in the payload alongside it so the alert can say what it was measured against.

The case this exists to kill: opens $150, dips to $141, now $149. Endpoint −0.67% (down), extreme-vs-low
+5.67% (up) — signs disagree, nothing fires. Without the rule that ticker fires a spurious *rise* alert
every single cycle, forever, held back only by the cooldown.

The case this exists to keep: opens $145, peaks $150, bottoms $141, now $142. Endpoint −2.07% is under any
sensible threshold, extreme-vs-high −5.33% is over it, and both point down — it fires. That is a real slide
off the window high that an endpoint-only comparison sleeps through, and it is the entire reason extremes
are in the design.

**What it gives up — and an earlier draft of this paragraph described it wrongly.** It said a ticker that
fell sharply and recovered fully, ending net up, "stays silent". It does not. Oldest 150, low 130, current
151: endpoint +0.67% (up), extreme-vs-low +16.15% (up), signs agree, threshold cleared — it fires a **rise**
at +16.15%. What stays silent is the *fall*, not the alert.

**That leaves one open question, and it is the user's.** A V-shaped recovery ending barely above where it
started reports as a large rise. The climb from 130 to 151 is real, and the alert text names the comparison
so nothing is hidden — but it is the same *shape* of artefact sign agreement was introduced to kill, and the
rule only kills the half where the two measurements disagree. Three ways to settle it:

- **Accept it.** A 16% climb off the low is information; the wording carries the caveat.
- **Require the endpoint move to clear some fraction of the threshold too** — say a fifth. Kills the
  V-recovery, and starts to converge on the endpoint-only comparison that extremes exist to beat.
- **Report the endpoint move and reference the extreme**, so the headline number is always the net move.

Nothing is blocked on it: `MoveVerdict` carries both figures either way. **No test was written for this
case on purpose** — a test would have settled it silently.

The rule lives in one pure static class, `MoveAssessment` in `Alerts.Application/Evaluation/`, with no I/O
and no clock, so the whole of it is unit-testable. **The test that pins it is not "an alert fired at −6%"** —
that test passes under all three candidate rules. It is a test that oscillates a price across the band
repeatedly and asserts the alert count stays bounded (Task 8).

### 2.3 DECISION — "same trading session" is a gap guard, not a market calendar

There is no market calendar in this codebase and adding one is a week of holidays, half-days and time zones
for a demo. The property actually needed is *the window is not straddling a period when nothing was
sampled*, and that is observable from the samples themselves.

`PriceWindow` therefore carries `LargestGap` — the longest interval between two adjacent samples in the
window. The guard is:

```
LargestGap > PollInterval × MaxMissedSamples   →   suppress
```

with `MaxMissedSamples = 3` by default. A Friday-close-to-Monday-open window has a 62-hour gap and is
rejected. A cycle that missed two polls is not. This is strictly better than a calendar for the failure it
actually protects against — a calendar says nothing about the provider having been unreachable for an hour
on a Tuesday.

### 2.4 DECISION — the feed-health signal is a log and a health field, not an alert

"Raises a feed-health signal" must not mean "sends the user a different kind of alert". A user who set a
price threshold did not ask to be told about the data pipeline, and the degradation UI is Phase 6's job.

So: when the newest sample in a window is older than `PollInterval × MaxMissedSamples`, evaluation suppresses
every price alert for that ticker, logs once per cycle at `Warning` with event id `5310`, and
`GET /api/marketdata/health` gains a `staleTickers` count. Nothing is written to `fired_alerts` and nothing
is pushed. Phase 6 turns that field into something a person sees.

### 2.5 DECISION — the retention-versus-window check lives in the host

Retention belongs to MarketData (`MarketData:Polling:RetentionMinutes`, default **75**). The cap on what a
user may configure belongs to Alerts (`Alerts:MaxWindowMinutes`, default **60**). Nothing inside either
module can see both, and passing one module's configuration into the other's `Add…Module` would create a
dependency out of a number.

`src/Api/Extensions/PollingConsistencyExtensions.cs` reads both keys straight from `IConfiguration` and
throws at startup if `RetentionMinutes <= MaxWindowMinutes`. It is called from `Program.cs` immediately
after `AddAlertsModule`. This is wiring, not a feature: it looks at two configuration values and compares
them, and it owns no types.

The failure it prevents is the silent one the phase file names — somebody raises the window, nobody raises
retention, and alerts stop firing with no error anywhere.

### 2.6 DECISION — build order is Contracts → Domain → Application → Infrastructure → Api, one commit each

`ModuleBoundaryTests.ExpectedAssemblies_AllLoadByName_SoNoRuleScansAnEmptySet` pins the count at 17. It goes
to 22 in **Task 1**, together with all five `.csproj` files, so the suite is green from the first commit
rather than red for the whole phase.

**`EmptyShells_AreExactlyThePhasesNotYetBuilt` changes on almost every commit of this phase, and the skip
count rises before it falls.** An earlier draft of this section claimed the list and the count of 2 both
stayed put. That was wrong, and wrong in the direction that matters: it reasoned only about
`Alerts.Contracts`, forgetting that Task 1 creates **four other** assemblies carrying no type at all.
Measured on the tree:

| After | Empty Alerts assemblies | Skips |
|---|---|---|
| Task 1 | `Domain`, `Application`, `Infrastructure`, `Api` | **9** |
| Task 3 | `Application`, `Infrastructure`, `Api` | **7** |
| Task 8 | `Infrastructure`, `Api` | **6** |
| Task 4 | `Api` | fewer again |
| Task 12 | none | back to **2** |

This is the test working as designed, not a regression: the expected list is hard-coded precisely so that
each assembly coming off it is a deliberate edit on the commit that fills it. Keep it in ordinal order — the
assertion compares in order, not as a set.

**Every task that puts the first type into an Alerts layer must edit `ModuleBoundaryTests.cs`, and it is two
lists, not one.** The assembly comes *off* `EmptyShells_AreExactlyThePhasesNotYetBuilt` and *onto*
`PopulatedAssemblies_AreNotEmptyShells`. Miss either and the build goes red on a test the task never named.
Tasks 3, 4, 8 and 12 each own one of these edits; the task text does not repeat it.

**A related and nastier consequence: a rule that skips is not a rule.** Task 7's original step 5 said to
"confirm rule 1 still passes, proving Alerts reaches only `.Contracts`". It proved nothing. With no types in
`Alerts.Application` the compiler trims the project reference straight out of assembly metadata, so rule 1
was reporting `[SKIP]` on that assembly, and only became live when Task 8 put two types there. Do not treat
a green rule over a layer you have not filled yet as evidence of anything.

**The check worth running is not the number.** It is that `Alerts.Contracts` never appears on the list at
all. If it does, `IWatchedTickerReader` did not land, and the two rules over that assembly are silently
enforcing nothing while reporting green.

### 2.7 DECISION — Redis keys, exactly as the data model names them

Ownership follows the prefix. MarketData owns everything under `marketdata:`; Alerts owns everything under
`alerts:`. Neither reads the other's keys.

| Key | Type | Written by | Lifetime |
|---|---|---|---|
| `marketdata:prices:{ticker}` | sorted set, score = epoch ms | the poller only | trimmed on write to `RetentionMinutes` |
| `marketdata:claim:{yyyyMMddHHmm}` | string | the poller | expires 2 × poll interval |
| `marketdata:cycle-inflight` | string | the poller | expires 5 × poll interval, deleted on clean exit |
| `alerts:cooldown:{userId}:{ticker}:{direction}` | string | the evaluator | expires after `Alerts:CooldownMinutes` |
| `alerts:ticket:{ticket}` | string | the ticket endpoint | 30 s, deleted on first read |
| `alerts:user:{userId}` | pub/sub channel | the publisher | — |

**A sorted-set member is `"{epochMs}:{price}"`, never the bare price.** Members must be unique; a ticker
hitting the same value twice would otherwise update the existing entry's score and silently erase the
earlier reading.

**Do not copy `RedisLastKnownPriceStore.Encode`'s shape** — an earlier draft said to, and it is wrong twice
over. That method documents an *InvariantCulture* hazard, not a uniqueness one; a plain string key has no
uniqueness problem, because overwriting is the entire point of it. And the two field orders are **opposite**:
the last-known store writes `{price}:{epochMs}` and decodes with `LastIndexOf(':')`, while the window member
is `{epochMs}:{price}` and decodes with `IndexOf(':')`. Anyone told to match its shape writes price-first.
The InvariantCulture point does carry across and must be honoured.

### 2.8 DECISION — configuration keys and defaults

Added to `src/Api/appsettings.json` as empty/`0` placeholders and to
`src/Api/appsettings.Development.json` with the values below. Both compose and Bicep carry them from Task 15.

| Key | Default | Why this value |
|---|---|---|
| `MarketData:Polling:IntervalSeconds` | `60` | One sample a minute over 20 tickers is 20 of Finnhub's 60 calls a minute, leaving the dashboard its share |
| `MarketData:Polling:RetentionMinutes` | `75` | The 60-minute cap plus margin, per §2.5 |
| `MarketData:Polling:MaxMissedSamples` | `3` | The gap guard of §2.3 and the stale guard of §2.4 |
| `MarketData:Polling:MinimumSamples` | `5` | "One stale point is not a window" |
| `Alerts:MaxWindowMinutes` | `60` | The phase file's cap: a move over days is a trend, not a sharp move |
| `Alerts:CooldownMinutes` | `15` | Long enough that a nudge twice inside it yields one alert, short enough to demo |
| `Alerts:HistoryLimit` | `50` | The `?limit=` ceiling on `GET /api/alerts` |

### 2.9 DECISION — the wire contract, pinned against a built client

Task 14 shipped before any server route existed, so the browser side is now the specification and these are
findings from a real client rather than guesses. Tasks 6, 9, 11, 12 and 13 must match them.

**`thresholdPercent` is a JSON number, and that is not a contradiction of the money rule.** `Program.cs` sets
`JsonNumberHandling.Strict`, so a quoted number on `PUT /api/alerts/settings` would be **rejected**. The rule
that survives is narrower than "percentages are strings": a figure the **server computes** travels as a
string, because that is where precision is lost; a value the **user typed** travels as a number, because the
server is about to parse it into a `decimal` either way. So `thresholdPercent` is a number, and
`changePercent` and `endpointPercent` are strings.

**The history record and the pushed record are two different shapes feeding one list, and one of them has to
give.** Task 11's `GetFiredAlertsResult` carries `Money`; Task 9's `AlertNotification` carries bare price
strings plus a single shared `currency`. The client prepends a pushed row into the history cache, so a cache
holding both renders two ways. The client currently carries a `toFiredAlert` adapter with a test pinning it.
**Decide server-side: make `AlertNotification` carry `Money` and delete the adapter, or keep both and keep
it.** Do not leave it undecided — a silent drift here renders every pushed row's price as an em dash, and
only for rows that arrived live, which is the hardest possible thing to notice.

Five smaller ones, each a real 4xx or a blank panel if missed:

- **`GET /api/alerts/settings` returns `200 []` for a user with no thresholds, never 404.** The portfolio
  page reads it on every mount.
- **`POST /api/alerts/simulate` must accept a body of `{"ticker": null}`** and pick a position itself. The
  client always sends a body with `Content-Type: application/json`, because a bodiless POST 415s against a
  required parameter. Answer 202 with no body; 409 when there is nothing to simulate.
- **`POST /api/alerts/stream-ticket` returns `{ ticket, expiresAt }`** and is sent with no body, like logout.
- **The alert frame must be a *named* `alert` event.** An unnamed frame arrives as `message` and the client
  never sees it. The `ping` frame costs nothing precisely because a named event with no listener is dropped
  by `EventSource` before it reaches any code.
- **`direction` arrives as `"Fall"` / `"Rise"`, not `0` / `1`.** `JsonStringEnumConverter` is registered so
  this holds today; it breaks the moment anyone serialises the enum by hand.

**`userId` on `AlertNotification` is redundant** — the stream is already per-user. The client types it
optional so the history record need not carry it.

---

## 3. File structure

### 3.1 The Alerts module — 5 projects, 46 files

```
src/Modules/Alerts/
  StockPortfolio.Modules.Alerts.Contracts/        no references at all
    IAlertEvaluator.cs                            inbound: "this ticker has a fresh sample"
    IWatchedTickerReader.cs                       outbound: "these tickers have an active alert"
    StockPortfolio.Modules.Alerts.Contracts.csproj

  StockPortfolio.Modules.Alerts.Domain/           → Shared.Kernel; package OneOf
    AlertDirection.cs                             enum Fall, Rise
    AlertSetting.cs                               entity: user + ticker + percent + window + enabled
    AlertSettingId.cs
    AlertWindow.cs                                value object over minutes, capped
    FiredAlert.cs                                 entity
    FiredAlertId.cs
    Ticker.cs                                     Alerts' own — Portfolio's is off-limits
    ThresholdPercent.cs                           value object, 0 < p ≤ 100
    StockPortfolio.Modules.Alerts.Domain.csproj

  StockPortfolio.Modules.Alerts.Application/      → Domain, Contracts, MarketData.Contracts,
                                                    Portfolio.Contracts; packages OneOf,
                                                    Microsoft.Extensions.Logging.Abstractions
    Abstractions/IAlertCooldownStore.cs
    Abstractions/IAlertPublisher.cs
    Abstractions/IAlertSettingRepository.cs
    Abstractions/IAlertStreamSubscriber.cs
    Abstractions/IFiredAlertRepository.cs
    Abstractions/IStreamTicketStore.cs
    Evaluation/AlertEvaluator.cs                  implements Contracts.IAlertEvaluator
    Evaluation/MoveAssessment.cs                  §2.2 — pure, no I/O, no clock
    Evaluation/MoveVerdict.cs
    Evaluation/WatchedTickerReader.cs             implements Contracts.IWatchedTickerReader
    History/Queries/GetFiredAlerts/GetFiredAlertsQuery.cs
    History/Queries/GetFiredAlerts/GetFiredAlertsQueryHandler.cs
    History/Queries/GetFiredAlerts/GetFiredAlertsResult.cs
    Settings/Commands/SaveAlertSetting/SaveAlertSettingCommand.cs
    Settings/Commands/SaveAlertSetting/SaveAlertSettingCommandHandler.cs
    Settings/Commands/SaveAlertSetting/SaveAlertSettingResult.cs
    Settings/Commands/SaveAlertSetting/TickerNotHeld.cs
    Settings/Commands/SaveAlertSetting/WindowExceedsRetention.cs
    Settings/Queries/GetAlertSettings/GetAlertSettingsQuery.cs
    Settings/Queries/GetAlertSettings/GetAlertSettingsQueryHandler.cs
    Settings/Queries/GetAlertSettings/GetAlertSettingsResult.cs
    Simulation/Commands/SimulateAlert/NoPositionToSimulate.cs
    Simulation/Commands/SimulateAlert/SimulateAlertCommand.cs
    Simulation/Commands/SimulateAlert/SimulateAlertCommandHandler.cs
    Streaming/AlertNotification.cs                the pub/sub payload record
    Streaming/Commands/IssueStreamTicket/IssueStreamTicketCommand.cs
    Streaming/Commands/IssueStreamTicket/IssueStreamTicketCommandHandler.cs
    Streaming/Commands/IssueStreamTicket/IssueStreamTicketResult.cs
    Streaming/Commands/RedeemStreamTicket/RedeemStreamTicketCommand.cs
    Streaming/Commands/RedeemStreamTicket/RedeemStreamTicketCommandHandler.cs
    Streaming/Commands/RedeemStreamTicket/TicketNotRecognised.cs
    StockPortfolio.Modules.Alerts.Application.csproj

  StockPortfolio.Modules.Alerts.Infrastructure/   → Application; packages EFCore, Npgsql, StackExchange.Redis
    AlertsModule.cs                               the only public type here
    AssemblyInfo.cs                               InternalsVisibleTo the unit tests
    Persistence/AlertsDbContext.cs
    Persistence/AlertsDbContextFactory.cs
    Persistence/AlertSettingRepository.cs
    Persistence/FiredAlertRepository.cs
    Persistence/Configurations/AlertSettingConfiguration.cs
    Persistence/Configurations/FiredAlertConfiguration.cs
    Persistence/Converters/AlertSettingIdConverter.cs
    Persistence/Converters/AlertWindowConverter.cs
    Persistence/Converters/FiredAlertIdConverter.cs
    Persistence/Converters/ThresholdPercentConverter.cs
    Persistence/Converters/TickerConverter.cs
    Persistence/Migrations/…InitialAlerts.cs      generated
    Redis/RedisAlertCooldownStore.cs
    Redis/RedisAlertPublisher.cs                  publish + subscribe, one connection
    Redis/RedisStreamTicketStore.cs
    StockPortfolio.Modules.Alerts.Infrastructure.csproj

  StockPortfolio.Modules.Alerts.Api/              → Application, Shared.Api; package FluentValidation
    AlertsEndpoints.cs
    AlertStream.cs                                the SSE loop and the 20-second heartbeat
    Requests/SaveAlertSettingRequest.cs
    Requests/SimulateAlertRequest.cs
    Validators/SaveAlertSettingRequestValidator.cs
    Validators/SimulateAlertRequestValidator.cs
    StockPortfolio.Modules.Alerts.Api.csproj
```

**`AlertDirection` needs a value converter**, and it is the case `CLAUDE.md` warns about: Identity has no
value object that is not an id, so "id" and "type needing a converter" happen to name the same set there.
Here they do not. Register `Properties<T>()` **and** `DefaultTypeMapping<T>()` for all five converters.

### 3.2 MarketData additions — 7 files

```
src/Modules/MarketData/StockPortfolio.Modules.MarketData.Contracts/
  IPriceWindowReader.cs                           new — Alerts' only read of MarketData

src/Modules/MarketData/…Application/
  Abstractions/IPollTargetSource.cs               new — §2.1
  Abstractions/IPriceSampleObserver.cs            new — §2.1
  Abstractions/IPriceWindowStore.cs               new
  Prices/PriceWindowReader.cs                     new — implements the contract

src/Modules/MarketData/…Infrastructure/
  Polling/PollingOptions.cs                       new — FromConfiguration, like FinnhubOptions
  Polling/QuotePoller.cs                          new — the only BackgroundService in src/
  Polling/RedisPollLease.cs                       new — the two locks of §2.7
  Prices/RedisPriceWindowStore.cs                 new
```

### 3.3 Host additions — 3 files, plus edits

```
src/Api/Adapters/AlertsPollTargetSource.cs        Alerts.Contracts → MarketData.IPollTargetSource
src/Api/Adapters/AlertsPriceSampleObserver.cs     MarketData.IPriceSampleObserver → Alerts.Contracts
src/Api/Extensions/PollingConsistencyExtensions.cs §2.5
```

Edited: `Program.cs` (three Alerts wire-ups, two adapter registrations, one consistency check, one
`MapAlertsEndpoints`), `Migrator/MigratedModules.cs`, `appsettings*.json`, both `Dockerfile`s,
`StockPortfolio.slnx`.

### 3.4 Frontend additions — 6 files, plus edits

```
src/Web/src/alerts/alertsApi.ts                   fetchers, alertKeys, queryOptions
src/Web/src/alerts/useAlertStream.ts              one connection for the whole app
src/Web/src/alerts/AlertPanel.tsx                 the dashboard panel
src/Web/src/alerts/AlertSettingsForm.tsx          threshold, window, on/off, per position
src/Web/src/alerts/LiveBadge.tsx                  "Live (SSE)" — never "WS Live"
src/Web/src/routes/_authenticated/notifications.tsx
```

Edited: `_authenticated.tsx` (open the stream once, inside the layout), `AppShell.tsx` (nav entry + badge),
`_authenticated/dashboard.tsx` (the panel), `portfolio.tsx` (a per-row threshold control).
New tests: `tests/alerts.test.tsx`, `tests/alertStream.test.ts`, `tests/msw/alerts.ts`.

---

## 4. Tasks

Each task ends green: `dotnet build` at 0 warnings and `dotnet test` passing, or `npm --prefix src/Web test`
for the frontend ones. Commit at the end of each.

> **Windows note.** `dotnet test` on the host fails with `0x800711C7` under Application Control. Run the
> suite in a Linux SDK container — see the memory note `dotnet-test-blocked-by-smart-app-control`.
>
> **Pass `-p:ArtifactsPath=<repo>/artifacts-linux` to container *builds* as well as container test runs.**
> A bare `dotnet build` inside the container writes Linux output into the host's `artifacts/`, mixing two
> platforms' binaries in one directory. The next host build then fails in ways that look like a code fault
> and are not. If it happens, delete `artifacts/` and rebuild — deleting is the point, since an incremental
> rebuild leaves a DLL whose inputs have not changed exactly where it is.

---

### Task 1 — Five empty Alerts projects, and the counts that move with them

**Files.** Create the five `.csproj` under `src/Modules/Alerts/` mirroring MarketData's exactly (see §3.1 for
each project's references). Create `StockPortfolio.Modules.Alerts.Infrastructure/AssemblyInfo.cs` with
`[assembly: InternalsVisibleTo("StockPortfolio.Modules.Alerts.UnitTests")]`. Create
`tests/StockPortfolio.Modules.Alerts.UnitTests/` mirroring `MarketData.UnitTests`.

Modify: `StockPortfolio.slnx` (six entries), `src/Api/Dockerfile` (five `COPY` lines),
`src/Migrator/Dockerfile` (the same five — **both**, and this is exactly the C3 duplication the register
warns about; it stays deferred, but miss one file and the container build breaks while `dotnet build` stays
green), `tests/StockPortfolio.Architecture.Tests/SolutionAssemblies.cs`.

**Steps.**

1. Add `"Alerts"` to `SolutionAssemblies.ModuleNames`, keeping it first so `Ordinal` ordering elsewhere is
   stable: `["Alerts", "Identity", "Portfolio", "MarketData"]`.
2. Change `ModuleBoundaryTests.cs:16-19` to `ShouldBe(22, "Four modules times five layers, plus
   Shared.Kernel and Shared.Api. …")`.
3. Add exactly one type to `Alerts.Contracts` so it is not an empty shell — `IWatchedTickerReader`, written
   in full here because Task 10 depends on the signature:

```csharp
namespace StockPortfolio.Modules.Alerts.Contracts;

/// <summary>The tickers somebody has an enabled threshold on. An empty list is the ordinary case.</summary>
public interface IWatchedTickerReader
{
    /// <summary>Reads every distinct ticker with at least one enabled setting, canonical upper case.</summary>
    Task<IReadOnlyList<string>> GetWatchedTickersAsync(CancellationToken ct);
}
```

4. Run `dotnet build` — expect 0 warnings, 32 projects.
5. Run `dotnet test`. **Expect `EmptyShells_AreExactlyThePhasesNotYetBuilt` to stay green with exactly two
   skips.** If it reports four skips, `Alerts.Contracts` compiled empty and step 3 did not land.
6. Commit: `chore(alerts): add the five Alerts projects and move the assembly count to 22`.

---

### Task 2 — Close C11 before anything can hide behind it

Do this **second**, before any endpoint exists. The whole point of C11 is that a module which is never
mapped passes every test; leaving the fix until the end means the rest of the phase is unprotected against
exactly the mistake it guards.

**Files.** Modify `tests/StockPortfolio.Api.IntegrationTests/EndpointMetadataTests.cs`.

**Steps.**

1. Replace the three literal `…RouteNames` arrays with a dictionary keyed by module name, and derive the set
   of modules that *must* appear from the loaded assemblies rather than from the dictionary:

```csharp
/// <summary>Every module that ships an .Api assembly must contribute at least one mapped route.</summary>
private static IReadOnlyList<string> MappedModules() =>
    AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetName().Name)
        .Where(name => name is not null
            && name.StartsWith("StockPortfolio.Modules.", StringComparison.Ordinal)
            && name.EndsWith(".Api", StringComparison.Ordinal))
        .Select(name => name!["StockPortfolio.Modules.".Length..^".Api".Length])
        .Order(StringComparer.Ordinal)
        .ToList();
```

2. Add the new test. This is the one that goes red when `MapAlertsEndpoints` is missing:

```csharp
[Fact]
public void EveryModuleWithAnApiAssembly_ContributesAtLeastOneMappedRoute()
{
    var mapped = _fixture.Endpoints
        .Select(endpoint => endpoint.Metadata.GetMetadata<IRouteNameMetadata>()?.RouteName)
        .Where(name => name is not null)
        .ToHashSet(StringComparer.Ordinal);

    foreach (var module in MappedModules())
    {
        ExpectedRouteNames[module].Any(mapped.Contains).ShouldBeTrue(
            module + " ships an .Api assembly and contributes no route to the host's EndpointDataSource. "
                + "The usual cause is a missing Map" + module + "Endpoints call in Program.cs — which "
                + "compiles, registers, and serves nothing.");
    }
}
```

3. The `.Api` assembly must actually be loaded for `GetAssemblies()` to see it. It is, because
   `Program.cs` calls `Map<M>Endpoints` — which is the circularity that makes this work: an unmapped module
   is also an unloaded one. Guard against a *vacuous* pass by asserting the derived list is non-empty and
   matches `SolutionAssemblies.ModuleNames.Length` minus nothing:
   `MappedModules().Count.ShouldBe(3);` — becomes 4 after Task 12.

   **This is not optional.** A rule that passes by finding nothing needs a companion assertion that fails if
   the search finds nothing; that is the lesson `ReferenceWalker_FindsEdgesThatDoExist` exists to carry.
4. Break it on purpose: comment out `app.MapMarketDataEndpoints()` in `Program.cs`, run the test, watch it
   go red naming MarketData. Restore.
5. Commit: `test(api): derive the mapped-module set from the loaded .Api assemblies (closes C11)`.

---

### Task 3 — Alerts.Domain, and the unit tests that pin it

**Files.** All eight `.cs` under `Alerts.Domain`; `tests/StockPortfolio.Modules.Alerts.UnitTests/` gains
`AlertSettingTests.cs`, `FiredAlertTests.cs`, `AlertWindowTests.cs`, `ThresholdPercentTests.cs`,
`TickerTests.cs`.

**Interfaces produced.**

```csharp
public enum AlertDirection { Fall = 0, Rise = 1 }

public readonly record struct AlertSettingId(Guid Value);
public readonly record struct FiredAlertId(Guid Value);

public readonly record struct Ticker            // Alerts' own; canonicalises in its own factory
{
    public string Value { get; }
    public static OneOf<Ticker, InvalidInput> Create(string? raw);
    internal Ticker(string canonical);           // the converter's read path, no validation
}

public readonly record struct ThresholdPercent  // 0 < p <= 100, two decimal places
{
    public decimal Value { get; }
    public static OneOf<ThresholdPercent, InvalidInput> Create(decimal raw);
}

public readonly record struct AlertWindow        // 1..maxMinutes
{
    public int Minutes { get; }
    public TimeSpan Duration => TimeSpan.FromMinutes(Minutes);
    public static OneOf<AlertWindow, InvalidInput> Create(int minutes, int maxMinutes);
}

public sealed class AlertSetting
{
    public AlertSettingId Id { get; }
    public Guid UserId { get; }                  // raw Guid: Alerts does not own the user
    public Ticker Ticker { get; }
    public bool Enabled { get; private set; }
    public ThresholdPercent Threshold { get; private set; }
    public AlertWindow Window { get; private set; }

    private AlertSetting(AlertSettingId id, Guid userId, Ticker ticker, bool enabled,
        ThresholdPercent threshold, AlertWindow window);   // assigns only — EF binds this

    public static OneOf<AlertSetting, InvalidInput> Create(
        Guid userId, string ticker, decimal thresholdPercent, int windowMinutes,
        bool enabled, int maxWindowMinutes);

    public OneOf<Success, InvalidInput> Adjust(
        decimal thresholdPercent, int windowMinutes, bool enabled, int maxWindowMinutes);
}

public sealed class FiredAlert
{
    public FiredAlertId Id { get; }
    public Guid UserId { get; }
    public Ticker Ticker { get; }
    public AlertDirection Direction { get; }
    public decimal ChangePercent { get; }        // the extreme move, signed
    public decimal EndpointPercent { get; }      // the endpoint move, signed — §2.2
    public Money TriggerPrice { get; }
    public Money ReferencePrice { get; }         // the window extreme it was measured against
    public DateTimeOffset FiredAt { get; }
    public bool IsSimulated { get; }

    private FiredAlert(/* every mapped value, assigning only */);

    public static FiredAlert Record(
        Guid userId, Ticker ticker, AlertDirection direction, decimal changePercent,
        decimal endpointPercent, Money triggerPrice, Money referencePrice,
        DateTimeOffset firedAt, bool isSimulated);
}
```

**Steps.**

1. Write `AlertWindowTests` first — it is the smallest and it pins the cap:
   `Create(61, maxMinutes: 60)` returns `InvalidInput` with `Field == "windowMinutes"`;
   `Create(0, 60)` and `Create(-5, 60)` likewise; `Create(60, 60)` succeeds.
2. Run: expect FAIL, `AlertWindow` does not exist.
3. Write `AlertWindow`. Run: PASS.
4. Repeat for `ThresholdPercent` (reject `0m`, `-1m`, `100.01m`; accept `0.01m` and `100m`), then `Ticker`
   (lower case in → upper case out; empty, 6-letter and digit-bearing input rejected — mirror
   `Portfolio.Domain.Ticker`'s rules exactly, and **write them out rather than referencing them**, because
   the two types are deliberately not shared).
5. `AlertSettingTests`: `Create` rejects a bad ticker, a bad percent and an over-cap window and reports the
   right `Field` each time; `Adjust` on a valid setting changes all three; `Adjust` with a bad value leaves
   the entity untouched.
6. `FiredAlertTests`: `Record` stores the sign of `ChangePercent` as given; `TriggerPrice` and
   `ReferencePrice` keep their currency; ids are distinct across two calls.
7. **`EfConstructorBindingTests` equivalent.** Add one test asserting each entity has exactly one
   constructor and that every parameter name matches a property name, case-insensitively. This is the trap
   that takes the host down at startup rather than on the first query.
8. Commit: `feat(alerts): domain entities and value objects`.

**TASKS 1–3 DONE** — commits `5b46862`, `c2b58a2`, `105eb41`. Build 0 warnings across 32 projects; suite
**531 passed, 7 skipped**, of which 54 are the new Alerts unit tests. Both deliberate-break steps were run
and both went red on the right thing.

Four corrections that later tasks depend on:

- **`Alerts.Domain`'s value objects keep a `public` positional constructor**, not the `internal` one written
  above. The converters live in `Alerts.Infrastructure`, a different assembly, and §3.1 lists no
  `AssemblyInfo.cs` under `.Domain` to grant it access — so `internal` would make the converter's read path
  uncompilable. This matches `Portfolio.Domain.Ticker` exactly: public constructor, validation only in
  `Create`. **Task 4's converters therefore need no change.**
- **The five Alerts projects also had to be added to `tests/StockPortfolio.Architecture.Tests/…csproj`.**
  That suite discovers assemblies by `Assembly.Load` and by enumerating its own output directory, so without
  the references `ExpectedAssemblies_AllLoadByName` fails on five names.
- **Task 2's snippet did not compile** against this tree in three ways, all now fixed in the file:
  `_fixture.Services.GetRequiredService<EndpointDataSource>()` rather than `_fixture.Endpoints`;
  `IEndpointNameMetadata`/`.EndpointName` rather than `IRouteNameMetadata`/`.RouteName`; and a `List<string>`
  return type, because CA1859 makes `IReadOnlyList<string>` a build error under `TreatWarningsAsErrors`.
- **The count assertion in Task 2 is load-bearing and must not be softened.** Commenting out
  `MapMarketDataEndpoints` leaves `MappedModules().Count` at 3, because `AddMarketDataApi()` keeps the
  assembly loaded. So the derived set catches a missing **Map**, and the count is the only thing that would
  catch a module wired nowhere at all. Task 12 raises it to 4 — raise it, do not replace it with
  "non-empty".

---

### Task 4 — Persistence, the migration, and the end of E1

**Files.** Everything under `Alerts.Infrastructure/Persistence/`, plus `AlertsModule.cs`,
`Migrator/MigratedModules.cs`, `appsettings.json`, `appsettings.Development.json`.

**Interfaces produced.**

```csharp
// Application/Abstractions/IAlertSettingRepository.cs
/// <summary>Saves before returning; there is no unit of work.</summary>
public interface IAlertSettingRepository
{
    Task<AlertSetting?> FindAsync(Guid userId, string ticker, CancellationToken ct);
    Task<IReadOnlyList<AlertSetting>> ListForUserAsync(Guid userId, CancellationToken ct);
    Task<IReadOnlyList<AlertSetting>> ListEnabledAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> ListEnabledTickersAsync(CancellationToken ct);
    Task SaveAsync(AlertSetting setting, CancellationToken ct);
}

// Application/Abstractions/IFiredAlertRepository.cs
public interface IFiredAlertRepository
{
    Task AddAsync(FiredAlert alert, CancellationToken ct);
    Task<IReadOnlyList<FiredAlert>> ListRecentAsync(Guid userId, int limit, CancellationToken ct);
}
```

**Steps.**

1. `AlertsDbContext` — copy `PortfolioDbContext` exactly, changing `SchemaName` to `"alerts"`. Keep
   `MigrationsHistoryTableName` and the `ConfigureWarnings(w => w.Throw(SkippedEntityTypeConfigurationWarning))`
   line. Register **all five** converters in `ConfigureConventions` with both `Properties<T>()` and
   `DefaultTypeMapping<T>()`.
2. `AlertSettingConfiguration` — table `alert_settings`; **unique index on `(UserId, Ticker)`**, because a
   threshold belongs to a position, not to an account; `threshold_percent` `HasPrecision(5, 2)`;
   `window_minutes` `int`; `enabled` `bool` default `true`.
3. `FiredAlertConfiguration` — table `fired_alerts`; **index on `(UserId, FiredAt descending)`**, which is
   the only way the history endpoint reads it; two `ComplexProperty` blocks for `TriggerPrice` and
   `ReferencePrice`, **members mapped explicitly** (`trigger_price_amount`, `trigger_price_currency`,
   `reference_price_amount`, `reference_price_currency`) — a bare `ComplexProperty` maps nothing and throws
   *"No suitable constructor was found"* at model build. Add a test asserting the mapped member count, per
   the trap.
4. Because `Money` is a `ComplexProperty`, **omit both money members from `FiredAlert`'s constructor**
   (efcore#31621) and let the factory assign them through `private set`. Model building fails at host
   startup otherwise, taking the whole process down.
5. `AlertsDbContextFactory` — reads `ConnectionStrings__Alerts`, falls back to the `migrator` string, and
   **repeats the `MigrationsHistoryTable(…, SchemaName)` call**. Omit it and four contexts share one history
   table (efcore#24127).
6. Generate the migration:

```bash
dotnet ef migrations add InitialAlerts --context AlertsDbContext --output-dir Persistence/Migrations --project src/Modules/Alerts/StockPortfolio.Modules.Alerts.Infrastructure --startup-project src/Api
```

7. `AlertsModule.AddAlertsModule(IServiceCollection, IConfiguration)` — `ConnectionStringName = "Alerts"`,
   eager throw on a blank string (Alerts genuinely cannot run without it), `AddDbContext<AlertsDbContext>`
   with the history-table call, the two repositories.
8. Add `services.AddAlertsModule(configuration)` to `Migrator/MigratedModules.cs` and
   `ConnectionStrings:Alerts` to the migrator's override dictionary in `Migrator/Program.cs`.
9. Add `"Alerts": ""` to `appsettings.json` and the real `alerts_svc` string to
   `appsettings.Development.json`, `Maximum Pool Size=2`.
10. Update `MigrationTests` — `historySchemas.ShouldBe(["alerts", "identity", "portfolio"])`.
11. Run the integration suite with Docker up. `SchemaIsolationTests` must still pass, which proves
    `alerts_svc` reaches `alerts` and nothing else.
12. **Verify E1 concretely**, not by reading this plan: `docker compose down -v && docker compose up`, then
    confirm the migrator reports three contexts and `/health/ready` is green.
13. Commit: `feat(alerts): persistence, the alerts schema and the first migration (closes E1)`.

**TASKS 4 AND 6 DONE** — commits `37a8623` and `5e69a65`. Build 0 warnings; suite **584 passed, 2 skipped**;
`docker compose down -v && up` from a clean volume reports `migrator: complete, 3 context(s) checked` with
`InitialAlerts` applied and `/health/ready` green; `psql -U alerts_svc` reads `alerts.alert_settings`.
**E1 is proven, not asserted** — Task 16 still owns ticking it off in the register.

Seven more corrections, four of which every later task inherits:

- **415 is not what an absent body returns — it is 400.** Only a *wrong* `Content-Type` produces 415.
  Tasks 11, 12 and 13 must declare accordingly. Worth knowing more widely: `POST /api/holdings` has declared
  415 since Phase 2 and **no test in the repo has ever driven it**; the Alerts settings pair is the first
  route where that declaration is actually demonstrated against a real request.
- **A generated migration does not compile here.** `CA1861` under `TreatWarningsAsErrors` rejects EF's inline
  `new[] { … }` for composite indexes — three of them in `InitialAlerts`, including the `descending:` array.
  `InitialPortfolio` carries the same hand-edit and no document mentioned it. **Regenerating any migration
  breaks the build until it is re-applied.**
- **§2.8's "empty or `0` placeholders in `appsettings.json`" is actively dangerous for
  `Alerts:MaxWindowMinutes`.** A `0` there overrides the code default and rejects every window a user could
  ask for. The real value (`60`) is in `appsettings.json` — it is not a secret, which is the only reason the
  neighbouring values are placeholders — and `AlertsModule` falls back to the code default on a missing or
  non-positive value. Task 15 must not "tidy" it back to a placeholder.
- **There are six converters, not the five §3.1 names.** `AlertDirection` needs an `EnumToStringConverter`,
  because `er-diagram.md` stores `direction` as `text "fall | rise"`. Register it with both `Properties<T>()`
  and `DefaultTypeMapping<T>()` like the rest.

Three smaller ones: Task 4 also owns the `Program.cs` wire-up and the `ProjectReference` from `src/Api` and
`src/Migrator`, without which `dotnet ef migrations add --startup-project src/Api` cannot see the context at
all; `ListEnabledTickersAsync` cannot be one query, because EF cannot see inside a value-converted type, so
`.Select(t => t.Value)` happens after materialising; and `AlertsModule` was **not** split the way Identity
was in Task 5, because nothing in it validates a runtime concern eagerly — the next module-level eager check
reopens that seam.

---

### Task 5 — Split `AddIdentityPersistence` out (closes C8)

**Files.** Modify `src/Modules/Identity/…Infrastructure/IdentityModule.cs`,
`src/Migrator/MigratedModules.cs`, `src/Migrator/Program.cs`,
`tests/StockPortfolio.Api.IntegrationTests/Infrastructure/ApiFixture.cs`.

**Steps.**

1. Extract `public static IServiceCollection AddIdentityPersistence(this IServiceCollection services,
   IConfiguration configuration)` containing only the connection-string read, the eager throw and
   `AddDbContext<IdentityDbContext>`. `AddIdentityModule` calls it first, then does everything else.
2. In `MigratedModules.AddEveryMigratedModule`, call `AddIdentityPersistence` and `AddPortfolioModule`.
   Portfolio has no eager non-persistence validation, so it needs no split. **`AddAlertsModule` is Task 4's
   line, not this one** — it does not exist while this task runs.
3. Delete the `Jwt:SigningKey` entry from the `overrides` dictionary in `Migrator/Program.cs`, and the
   `"migrator-placeholder-signing-key-unused-32b"` literal with it.
4. Delete `MigratorConfiguration`'s matching `Jwt:SigningKey` entry in `ApiFixture`.
5. Run `MigratorTests` and `MigrationTests`. Both contexts must still be found and applied. The `Count == 0`
   guard in `Migrator/Program.cs` catches a botched split loudly, and `MigratorTests` asserts non-empty
   *before* comparing, so it cannot pass on two empty lists.
6. Commit: `refactor(identity): split persistence out of the module registration (closes C8)`.

**DONE — branch `worktree-agent-a50a9980c22672580`, commit `0c93efd`. Build 0 warnings; 472 passed, 2
skipped.** Awaiting merge behind Task 1.

Two findings that Task 16 must carry into the register rather than repeat from it:

**C8's claim that the split "tightens the `ServiceCollection` walk" is false.** The walk filters on
`ServiceType.IsSubclassOf(typeof(DbContext))`, and nothing `AddIdentityModule` registered was ever a
`DbContext` — `JwtOptions`, two repositories, the hasher, the token issuer and six closed-generic handlers
are all unrelated types. The filter had no false positive to reject before and has none now. The collection
is about eleven descriptors smaller and the walk's *result* is identical.

**What the split actually buys.** `JwtOptions.FromConfiguration` is the argument to `AddSingleton`, so it
runs at *registration* time — every migrator run was parsing, base64-decoding and length-checking a signing
key before touching a database. That is now unreachable, not merely fed a dummy value. And the migrator's
contract with a module is now "give me a connection string", so no future module's eager validation can
force a new placeholder line back into `Migrator/Program.cs`. That second point is what C8's trigger was
about, and it is why this belonged in Phase 4 rather than earlier.

**Honest cost.** Running against a bare `ServiceCollection` used to prove incidentally that `Add<M>Module`
was self-contained — that it leaned on no host-registered service. That proof now covers only Identity's
persistence half. `ApiFixture` builds the real host and would catch a missing host service by a different
route, so this narrows coverage rather than opening a hole, but it is a real change in what the migrator
seam enforces for free.

---

### Task 6 — The settings slice: `GET` and `PUT /api/alerts/settings`

**Files.** The seven files under `Application/Settings/`, `Api/Requests/SaveAlertSettingRequest.cs`,
`Api/Validators/SaveAlertSettingRequestValidator.cs`, and the first half of `Api/AlertsEndpoints.cs`.

**Interfaces produced.**

```csharp
public sealed record SaveAlertSettingCommand(
    Guid UserId, string Ticker, decimal ThresholdPercent, int WindowMinutes, bool Enabled);

public sealed record SaveAlertSettingResult(
    string Ticker, decimal ThresholdPercent, int WindowMinutes, bool Enabled);

public sealed record TickerNotHeld(string Ticker);
public sealed record WindowExceedsRetention(int RequestedMinutes, int MaximumMinutes);

// handler signature — every case spelled out, no named union
ICommandHandler<SaveAlertSettingCommand,
    OneOf<SaveAlertSettingResult, TickerNotHeld, WindowExceedsRetention, InvalidInput>>
```

**Steps.**

1. `SaveAlertSettingCommandHandler` injects `IUserHoldsTicker` (already published by
   `Portfolio.Contracts` — this is the phase's only Alerts → Portfolio call), `IAlertSettingRepository` and
   the max-window value. Order: shape is already done by the filter, so ask `HoldsAsync` first and return
   `TickerNotHeld` before touching the database.
2. `GetAlertSettingsQueryHandler` returns `IReadOnlyList<GetAlertSettingsResult>` — no failure case; a user
   with no thresholds has none, which is not an error.
3. `SaveAlertSettingRequestValidator` checks shape only: ticker 1–5 letters, percent `> 0` and `<= 100`,
   window `>= 1`. **The cap is not a shape rule** — it depends on configuration and belongs in the handler,
   as `WindowExceedsRetention`.
4. Map both routes in `AlertsEndpoints.cs`, `.RequireAuthorization()`, with `.Produces` for every status:
   200, 400, 401, 409 (`TickerNotHeld`), 415, 500. Name them `GetAlertSettings` and `SaveAlertSetting`.
5. Wire `AddAlertsApi` and `MapAlertsEndpoints` into `Program.cs`. Add both names to
   `EndpointMetadataTests`' dictionary and bump the count assertion from Task 2 step 3 to 4.
6. Integration tests: setting a threshold on a ticker you do not hold returns 409; a 61-minute window
   returns 409 naming both numbers; a valid save round-trips through `GET`; saving twice updates rather than
   duplicating (the unique index proves it).
7. Read `/openapi/v1.json` and confirm every declared status matches what a real request returns.
8. Commit: `feat(alerts): alert settings endpoints`.

---

### Task 7 — The price window: MarketData's new store and contract

**Files.** The five new MarketData files of §3.2 except the poller and the lease.

**Interfaces produced.**

```csharp
// MarketData.Contracts/IPriceWindowReader.cs — records of primitives only
public sealed record PriceWindow(
    string Ticker,
    decimal Current,
    decimal Oldest,
    decimal Low,
    decimal High,
    DateTimeOffset OldestAt,
    DateTimeOffset NewestAt,
    int SampleCount,
    TimeSpan LargestGap);

/// <summary>The one history read Alerts makes of MarketData. A ticker with no series is absent.</summary>
public interface IPriceWindowReader
{
    Task<PriceWindow?> GetWindowAsync(string ticker, TimeSpan window, CancellationToken ct);
}

// MarketData.Application/Abstractions/IPriceWindowStore.cs
public interface IPriceWindowStore
{
    Task AppendAsync(string ticker, decimal price, DateTimeOffset at, TimeSpan retention, CancellationToken ct);
    Task<IReadOnlyList<(DateTimeOffset At, decimal Price)>> ReadAsync(
        string ticker, DateTimeOffset since, CancellationToken ct);
}
```

**Steps.**

1. `RedisPriceWindowStore` — sorted set at `marketdata:prices:{ticker}`, score = `at.ToUnixTimeMilliseconds()`,
   **member = `$"{epochMs}:{price}"`**. `AppendAsync` does `SortedSetAddAsync` then
   `SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, cutoffMs)` in one batch, and sets a key
   expiry of `retention × 2` so a ticker that stops being watched disappears on its own.
2. Unit-test the encoding first: two samples at the same price and different times produce **two** members,
   not one. That is the trap; assert it before writing the store.
3. `PriceWindowReader` computes `Current`, `Oldest`, `Low`, `High`, `SampleCount` and `LargestGap` from the
   read list in one pass. Returns `null` for an empty series — not a zero-filled window.
4. Register both in `AddMarketDataModule`: `IPriceWindowStore` singleton, `IPriceWindowReader` scoped.
5. ~~Add `Alerts.Application` → `MarketData.Contracts` to the csproj.~~ **Already done in Task 1**, which
   built every project's references from §3.1. Just confirm `ModuleBoundaryTests` rule 1 still passes,
   proving Alerts reaches only `.Contracts`.
6. Commit: `feat(marketdata): trimmed price windows in Redis`.

---

### Task 8 — `MoveAssessment`: the sign-agreement rule, and the test that can actually fail on it

This is the heart of the phase and the only part with no I/O. Write it test-first and in isolation.

**Files.** `Alerts.Application/Evaluation/MoveAssessment.cs`, `MoveVerdict.cs`;
`tests/StockPortfolio.Modules.Alerts.UnitTests/MoveAssessmentTests.cs`.

**Interfaces produced.**

```csharp
public sealed record MoveVerdict(
    bool Fires,
    AlertDirection Direction,
    decimal ExtremePercent,
    decimal EndpointPercent,
    decimal ReferencePrice,
    string Reason);          // "fell 5.33% from the window high" — named in the alert text

public static class MoveAssessment
{
    /// <summary>Applies the sign-agreement rule to one window against one threshold.</summary>
    public static MoveVerdict Assess(PriceWindow window, decimal thresholdPercent);
}
```

**Steps.**

1. Write the four table-driven cases of §2.2 first, with the exact numbers, and **write the oscillation
   test before the implementation exists**:

```csharp
[Fact]
public void OscillatingInsideTheBand_DoesNotFireEveryCycle()
{
    // 150 -> 141 -> 149 -> 141 -> 149 …  Threshold 5%.
    // Against the window low alone this fires a +5.67% RISE on every up-leg, forever.
    var fires = 0;

    foreach (var current in new[] { 149m, 141m, 149m, 141m, 149m, 141m })
    {
        var window = new PriceWindow(
            Ticker: "AAPL", Current: current, Oldest: 150m, Low: 141m, High: 150m,
            OldestAt: Origin, NewestAt: Origin.AddMinutes(60), SampleCount: 60,
            LargestGap: TimeSpan.FromMinutes(1));

        if (MoveAssessment.Assess(window, 5m).Fires) { fires++; }
    }

    // Only the down-legs agree in sign. Without sign agreement this is 6.
    fires.ShouldBe(3);
}
```

2. Run it. Expect FAIL — `MoveAssessment` does not exist.
3. Add the three single-shot cases: the $150→$141 fall fires at −6%; the $145/$150/$141→$142 case fires at
   −5.33% **even though the endpoint move is only −2.07%**; the $140→$149 rise fires at +6.43%.
4. Add the case that proves the rule is doing work rather than the threshold: a window where endpoint and
   extreme disagree and the extreme is **well** over the threshold must not fire.
5. Implement `Assess`. Compute both moves, take the extreme with the larger magnitude, compare signs, then
   compare `|extreme|` to the threshold. `Reason` names which comparison won.
6. Run: PASS. Then **mutate it** — delete the sign comparison and confirm the oscillation test goes red and
   the other three stay green. If deleting the rule leaves everything green, the test does not pin the rule
   it is named after.
7. Commit: `feat(alerts): sign-agreement move assessment`.

**SCAFFOLDING DONE — branch `worktree-agent-a8ce066249a339e27`, commits `ab6d7c3` (Task 7) and `89df011`.**
Task 7 is complete, including a mutation run that proves the member-uniqueness tests catch a bare-price
encoding while four sibling window tests stay green — an erased reading is invisible to any assertion about
prices and ordering alone, which is the same blind spot `Dashboard_ProviderReturns429` had.

`Assess` is a stub throwing `NotImplementedException`, so **six `MoveAssessmentTests` fail by design** and
the build stays at 0 warnings. That is the red state, not a regression. Two `// TODO(you):` markers sit
inside `Assess` at the points where the remaining choices live.

Two things the plan never specified and nobody should guess:

- **What `MoveVerdict` holds when `Fires` is false.** `Direction`, `ExtremePercent`, `EndpointPercent` and
  `ReferencePrice` are all non-nullable. The non-firing tests assert `Fires` alone, deliberately.
- **Whether `Assess` rounds.** The worked figures are quoted to two decimals; the tests use a 0.005
  tolerance, so a rounding and a non-rounding implementation both read as correct. Pin it or leave it.

And one convention divergence, built as specified rather than silently corrected:
`IPriceWindowStore.ReadAsync` returns `IReadOnlyList<(DateTimeOffset At, decimal Price)>` and takes a
`string` ticker. It is the only tuple-returning abstraction in the codebase — every other MarketData
abstraction returns a named type and takes the `Ticker` value object.

---

### Task 9 — The evaluator: guards, cooldown, persist-then-publish

**Files.** `Alerts.Application/Evaluation/AlertEvaluator.cs`, `WatchedTickerReader.cs`;
`Abstractions/IAlertCooldownStore.cs`, `IAlertPublisher.cs`; `Streaming/AlertNotification.cs`;
`Alerts.Infrastructure/Redis/RedisAlertCooldownStore.cs`, `RedisAlertPublisher.cs`.

**Interfaces produced.**

```csharp
public interface IAlertCooldownStore
{
    /// <summary>Sets the cooldown only if absent, and reports whether this caller won. One round trip.</summary>
    Task<bool> TryStartAsync(Guid userId, string ticker, AlertDirection direction,
        TimeSpan cooldown, CancellationToken ct);
}

public interface IAlertPublisher
{
    Task PublishAsync(AlertNotification notification, CancellationToken ct);
}

public sealed record AlertNotification(
    Guid Id, Guid UserId, string Ticker, string Direction, string ChangePercent,
    string EndpointPercent, string TriggerPrice, string ReferencePrice,
    string Currency, DateTimeOffset FiredAt, bool IsSimulated, string Reason);
```

Money and percentages travel as **strings** through the pub/sub payload too, for the same reason they do
over HTTP.

**Steps.**

1. `AlertEvaluator.EvaluateAsync(ticker, ct)` — implements `Contracts.IAlertEvaluator`. Order is fixed:
   read every enabled setting for this ticker; read the window **once** for the longest window among them;
   run the three guards; then per setting, `MoveAssessment.Assess`, cooldown, record, publish.
2. The three guards, in this order, each with its own unit test:
   - `SampleCount < MinimumSamples` → return, no log.
   - `LargestGap > PollInterval × MaxMissedSamples` → return (§2.3).
   - `now − NewestAt > PollInterval × MaxMissedSamples` → log `Warning` id `5310`, return (§2.4).
3. **`TryStartAsync` is a single `StringSetAsync(key, "1", ttl, When.NotExists)`.** A read-then-write would
   let two replicas both pass the check for the same user and ticker in the same millisecond and send two
   alerts. Do not split it.
4. **Persist, then publish.** `IFiredAlertRepository.AddAsync` saves before `IAlertPublisher.PublishAsync`
   is called, and a publish failure is caught and logged, never rethrown. The row is what matters; the push
   only decides whether it also arrives now.
5. `WatchedTickerReader` is one call to `IAlertSettingRepository.ListEnabledTickersAsync`.
6. Unit-test the evaluator against in-memory fakes for all five abstractions. **This is the first module
   with handler-level fakes**; put them in `tests/StockPortfolio.Modules.Alerts.UnitTests/Fakes/` and follow
   the shape B4/B6 in the deferred register asks for, so that item gets cheaper rather than staler.
7. Use `FakeTimeProvider` for every clock. Do not add another `TestClock`.
8. Commit: `feat(alerts): evaluation, guards, cooldown and publication`.

---

### Task 10 — The poller, the two locks, and `minReplicas: 1`

**Files.** `MarketData.Infrastructure/Polling/{PollingOptions,QuotePoller,RedisPollLease}.cs`;
`src/Api/Adapters/AlertsPollTargetSource.cs`, `AlertsPriceSampleObserver.cs`;
`src/Api/Extensions/PollingConsistencyExtensions.cs`; `infra/main.bicep`.

**Steps.**

1. `PollingOptions.FromConfiguration` follows `FinnhubOptions`' shape exactly — a static factory, no
   `IOptions<T>` binding, silent defaults per §2.8. A configuration class with constructor parameters is
   skipped in silence by the binder, which is why nothing here is bound.
2. `RedisPollLease` implements the **two** locks of §2.7 and they are not interchangeable:
   - `marketdata:claim:{yyyyMMddHHmm}` set with `When.NotExists` picks one winner *within* a minute.
   - `marketdata:cycle-inflight` set with `When.NotExists` and deleted in a `finally` stops a cycle that
     overran from being joined by the next minute's cycle on another replica. The first key says nothing
     across minute boundaries.
   Both carry an expiry as the backstop for a process that dies mid-cycle.
3. `QuotePoller : BackgroundService`. **The `try/catch` goes inside the loop, not around it** — an unhandled
   exception in a `BackgroundService` kills the host, because `StopHost` is the default.
4. Per cycle: acquire both leases; `IPollTargetSource.GetPollTargetsAsync`; return immediately on an empty
   list; fetch through the existing token bucket; for each success write **both**
   `IPriceWindowStore.AppendAsync` and `ILastKnownPriceStore` — the same single writer the dashboard uses,
   so the fake and real provider paths cannot record differently; then `IPriceSampleObserver`.
5. Register in `AddMarketDataModule`: `AddHostedService<QuotePoller>()`, and a no-op
   `IPriceSampleObserver` **only if none is already registered** (`TryAddSingleton`), so the host's adapter
   wins.
6. The two host adapters are four lines each and hold no logic:

```csharp
// src/Api/Adapters/AlertsPollTargetSource.cs
internal sealed class AlertsPollTargetSource(IWatchedTickerReader reader) : IPollTargetSource
{
    public Task<IReadOnlyList<string>> GetPollTargetsAsync(CancellationToken ct) =>
        reader.GetWatchedTickersAsync(ct);
}
```

   The poller is a singleton and `IWatchedTickerReader` is scoped, so the adapter takes `IServiceScopeFactory`
   and opens a scope per cycle. **Register the adapters after all three module calls in `Program.cs`**, and
   add a comment saying so — this is exactly the silent host assumption `CLAUDE.md` records for MarketData
   and `IConnectionMultiplexer`.
7. `PollingConsistencyExtensions.ValidateAlertWindowFitsRetention` per §2.5, called right after
   `AddAlertsModule`. Test it by setting `RetentionMinutes` below `MaxWindowMinutes` in a test host and
   asserting startup throws with both numbers in the message.
8. **`infra/main.bicep`: change `minReplicas: 0` to `1`** and rewrite the comment, which currently argues
   for 0 on the grounds that nothing runs in the background. That is no longer true, and this is the single
   line that decides whether alerts work on the deployed site.
9. Integration test: with no alert settings anywhere, run a cycle and assert `IQuoteProvider` was called
   **zero** times and no `marketdata:prices:*` key exists. That is the phase's "nothing polls" condition and
   it is the cheapest one to regress.
10. Commit: `feat(marketdata): the quote poller, its two locks, and an always-on replica`.

**STEPS 1–5 DONE — branch `worktree-agent-aa3485da71659e6eb`, commit `31e5360`.** Build 0 warnings; suite
**638 passed, 2 skipped**. Steps 6–10 remain.

**Two corrections that would each have silently disabled alerting**, and they are the reason to read this
block before writing the adapters:

- **Step 5's justification for `TryAddSingleton` is wrong.** `TryAdd` skips when the *service type* is
  already registered, and step 6 registers the adapters **after** the module calls — so `TryAdd` always runs
  first and always adds the no-op. What makes the host's adapter win is last-registration-wins plus the host
  using a plain `Add`. **Write `TryAddScoped` for `AlertsPriceSampleObserver` and the no-op wins, no alert
  ever evaluates, and nothing fails.** Pinned by `Module_HostRegistersAnObserverAfterwards_TheHostsWins`.
- **Step 6 contradicts itself inside one bullet.** The four-line sample constructor-injects a scoped
  `IWatchedTickerReader`; the next sentence says the adapter takes `IServiceScopeFactory`. A singleton
  adapter cannot do both. **Resolved in favour of the sample: the *poller* takes `IServiceScopeFactory` and
  resolves both ports from a per-cycle scope, so both adapters stay four lines and register as `scoped`.**

Three more the plan never said:

- **`IQuoteProvider` must not be captured by the poller.** With a real key it is a **transient** typed
  `HttpClient`; injecting it into a singleton pins one `HttpClient` for the process and defeats handler
  rotation. Resolve it from the per-cycle scope.
- **Lock order is claim-then-in-flight, and `ReleaseAsync` runs only after a successful acquire.** Releasing
  after a *refused* claim deletes the winner's in-flight key and re-opens the exact overlap the second key
  exists to prevent. Pinned by `Cycle_LeaseRefused_DoesNotEvenAskWhatToPoll`.
- **`FakeTimeProvider` plus `BackgroundService` has a startup race.** A single `Advance(interval)` after
  `StartAsync` loses the tick — `PeriodicTimer` does not buffer one that arrives before the service
  registered its wait, and neither that moment nor a cycle's end is observable. Advance until the cycle
  count is seen.

And three scope corrections: §3.2 lists three poller files but five were needed (`IPollLease` is the seam
that keeps the poller's unit tests off a live Redis, and `NoOpPriceSampleObserver` has no slot in the list);
**§2.8 lists four `MarketData:Polling:*` keys but MarketData reads two** — `MaxMissedSamples` and
`MinimumSamples` are the Alerts evaluator's, read straight from `IConfiguration`, because `PollingOptions`
is `internal`; and **§2.4's `staleTickers` field on `/api/marketdata/health` is unbuildable as specified**,
since only Alerts could compute it and MarketData depends on nothing. **The log at `Warning` 5310 is the
whole feed-health signal for this phase.** MarketData can judge its own windows' staleness later if the
field is ever wanted.

Two smaller notes: `Microsoft.Extensions.Hosting.Abstractions` was neither in `Directory.Packages.props` nor
referenced — `BackgroundService` compiled only because `Microsoft.Extensions.Http.Resilience` dragged it in
transitively, the exact case the repo's own `System.Threading.RateLimiting` comment says to pin. And the
per-ticker `try/catch` in the poller is what actually enforces "a failed observer must not stop the next
ticker"; on the interface it is only a comment.

---

### Task 11 — `GET /api/alerts?limit=50`

**Files.** The three files under `Application/History/`, plus the route in `AlertsEndpoints.cs`.

**Steps.**

1. `GetFiredAlertsQuery(Guid UserId, int Limit)`; the handler clamps `Limit` to `[1, Alerts:HistoryLimit]`
   rather than rejecting it — an out-of-range limit is a clamp, not a 400.
2. `GetFiredAlertsResult` carries money and percentages as `Money` (serialised as strings by the converter)
   and `FiredAt` as `DateTimeOffset`.
3. The query reads through the `(UserId, FiredAt desc)` index and projects the two `Money` complex
   properties' **members**, rebuilding `Money` afterwards, to keep `Money`'s `ToUpperInvariant()` off the
   per-row load path.
4. Route name `GetAlerts`. Declares 200, 401, 500.
5. Commit: `feat(alerts): fired-alert history`.

---

### Task 12 — The ticket handshake and the stream

**Files.** `Application/Streaming/Commands/{IssueStreamTicket,RedeemStreamTicket}/…`,
`Abstractions/IStreamTicketStore.cs`, `IAlertStreamSubscriber.cs`,
`Infrastructure/Redis/RedisStreamTicketStore.cs`, the subscribe half of `RedisAlertPublisher.cs`,
`Api/AlertStream.cs`, and the last two routes in `AlertsEndpoints.cs`.

**Steps.**

1. `IssueStreamTicketCommandHandler` — bearer-authenticated, generates 32 bytes from
   `RandomNumberGenerator`, base64url, stores `alerts:ticket:{ticket}` → user id with a **30-second**
   expiry, returns the ticket and its expiry.
2. `RedeemStreamTicketCommandHandler` — `StringGetDeleteAsync` in **one** call. A read followed by a delete
   lets two connections redeem the same ticket. Returns `OneOf<Guid, TicketNotRecognised>`.
3. `GET /api/alerts/stream?ticket=…` is `.AllowAnonymous()` — the ticket is the authentication. This is the
   only anonymous authenticated route in the application, and the comment on it must say why: the browser's
   event-source client cannot set a header and the SPA and API are on different origins permanently.
4. The stream body:

```csharp
// Api/AlertStream.cs — a named ping event, because SseFormatter has no comment API.
private static async IAsyncEnumerable<SseItem<object>> StreamAsync(
    Guid userId,
    IAlertStreamSubscriber subscriber,
    TimeProvider clock,
    [EnumeratorCancellation] CancellationToken ct)
{
    var channel = Channel.CreateUnbounded<AlertNotification>();
    await using var subscription = await subscriber.SubscribeAsync(userId, channel.Writer, ct);

    // 20s, against the platform's 4-minute idle close. 4 minutes is both the default AND the floor.
    using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(20), clock);

    while (!ct.IsCancellationRequested)
    {
        var next = channel.Reader.ReadAsync(ct).AsTask();
        var tick = heartbeat.WaitForNextTickAsync(ct).AsTask();

        if (await Task.WhenAny(next, tick) == next)
        {
            yield return new SseItem<object>(await next, eventType: "alert");
        }
        else
        {
            yield return new SseItem<object>(new { at = clock.GetUtcNow() }, eventType: "ping");
        }
    }
}
```

   Return it with `TypedResults.ServerSentEvents(StreamAsync(...))`.
5. `RedisAlertPublisher` also implements `IAlertStreamSubscriber`, subscribing to `alerts:user:{userId}` on
   the shared multiplexer. **Fan-out is mandatory, not an optimisation**: an alert produced on replica A
   while the user's stream is held by replica B is otherwise silently lost, and only for some users.
6. Add `CreateStreamTicket` and `StreamAlerts` to `EndpointMetadataTests`' dictionary. **Bump the
   `MappedModules().Count.ShouldBe(3)` from Task 2 to `4`** — and before doing so, run it at 3 and watch it
   fail, which is the proof Alerts is genuinely mapped.
7. Integration test: a ticket redeems once and the second attempt is rejected; an expired ticket is
   rejected; a connected stream receives an alert published on a *different* multiplexer subscription,
   which is what proves fan-out.
8. Commit: `feat(alerts): stream tickets and the server-sent-events feed`.

---

### Task 13 — Simulate

**Files.** The four files under `Application/Simulation/`, `Api/Requests/SimulateAlertRequest.cs`,
`Api/Validators/SimulateAlertRequestValidator.cs`, the route.

**Steps.**

1. `SimulateAlertCommandHandler` picks the caller's first enabled setting (or the ticker named in the
   request, if given and held), synthesises a plausible move at the threshold, and sends it through the
   **real** path — `FiredAlert.Record(..., isSimulated: true)`, saved, then published. Not a fake push to
   the socket, which would prove nothing about the mechanism.
2. Returns `OneOf<Success, NoPositionToSimulate>` → 202 or 409.
3. Integration test: simulate, then `GET /api/alerts` and find the row with `isSimulated: true`. That is
   also the phase's "simulate with the tab closed, then open the app" condition.
4. Commit: `feat(alerts): the simulate endpoint`.

---

### Task 14 — Frontend

**Files.** The six new files of §3.4 plus edits and three test files.

**Steps.**

1. `alertsApi.ts` — `alertKeys = { all: ['alerts'], history: () => [...all, 'history'], settings: () => [...all, 'settings'] }`,
   plus `alertHistoryQuery` and `alertSettingsQuery` as `queryOptions`, matching the house convention.
2. `useAlertStream.ts` — **one connection for the whole application**, opened in `_authenticated.tsx`, never
   per component. Three things it must do, each with its own test:
   - A `cancelled` flag and `clearTimeout` in cleanup. React 19 StrictMode runs effects twice and a held-open
     stream permanently occupies one of the browser's six connections per origin.
   - **Do not rely on `EventSource`'s built-in reconnect.** The ticket in the URL is spent by the time it
     retries. On `error`, close, fetch a fresh ticket, reopen with backoff.
   - Ignore `ping` events; on `alert`, `queryClient.setQueryData(alertKeys.history(), …)` prepending the row.
3. `AlertPanel.tsx` — the dashboard's right-hand column in the mockup. Titled around **recent activity**,
   not *active alerts*: a price alert is a moment that passed, and every row carries a timestamp. Simulated
   rows carry a badge. Usable at 375px.
4. `AlertSettingsForm.tsx` — a native `<select>` for the window and `<input role="switch">` for on/off. No
   component library.
5. `notifications.tsx` — the same data with a longer history, added to `AppShell`'s `NAV`.
6. `LiveBadge.tsx` — reads the hook's connection state and renders **"Live (SSE)"**. Never "WS Live";
   consistency between what is claimed and what was built is graded.
7. Tests: a burst of alert events renders in order; a `ping` renders nothing; a dropped connection refetches
   history rather than replaying; the panel is present on the dashboard and the notifications route lists
   more rows; the badge text is exactly `Live (SSE)`.
8. Run `npm --prefix src/Web test`. Expect 37 + the new tests passing.
9. Commit: `feat(web): the alert stream, panel and notifications screen`.

---

### Task 15 — Infrastructure

**Files.** `docker-compose.yml`, `.env.example`, `infra/modules/containerapp-api.bicep`,
`infra/main.bicep`, `infra/main.bicepparam`, `.github/workflows/deploy.yml`, `src/Web/nginx.conf`.

**Steps.**

1. Add the seven configuration keys of §2.8 to the `api` service in compose and to `baseEnv` in
   `containerapp-api.bicep`. `ConnectionStrings__Alerts` and `ALERTS_PW` are already there — they are what
   E1 was tracking, and Task 4 gave them an owner.
2. **Raise the scale rule's `concurrentRequests` from `100`.** A held-open stream may count as one in-flight
   request for its entire life, so at 100 a few dozen connected browsers scale on *user count* rather than
   load. Set it to `400` and leave `maxReplicas: 2`, which the connection budget requires anyway.
3. Update the connection-budget arithmetic wherever it is written down: three registered contexts × pool 2 ×
   2 replicas = **12** of 35. **Count `AddDbContext` calls; do not restate the figure from memory** — it has
   been published wrong before.
4. Confirm `nginx.conf`'s `location /api/alerts/stream` block is genuinely buffering-off. It has been there
   since Phase 1 and has never carried traffic; with buffering on, events queue and nothing arrives until
   the response ends, which for a stream is never.
5. Grep `src/` for `UseResponseCompression`. Expect zero hits, now and forever.
6. `az deployment group what-if -g stockportfolio-rg -f infra/main.bicep` before pushing. A parameter a
   workflow passes but the template no longer declares fails at preflight, not at runtime.
7. Commit: `chore(infra): polling configuration, an always-on replica and a stream-aware scale rule`.

---

### Task 16 — Documents, and the register

**Files.** `README.md`, `CLAUDE.md`, `docs/plan/00-overview.md`, `docs/plan/phase-4-alerts.md`,
`docs/deferred-work.md`, `docs/reference/{er-diagram,module-interactions,module-boundaries}.md`.

**Steps.**

1. `README.md` records the five things `phase-4-alerts.md` §8 requires: why a one-way stream rather than
   WebSockets; the ticket handshake and why it exists; the heartbeat and the four-minute platform limit; why
   replay was dropped; and **which false-positive constraint was chosen and why** — sign agreement, with the
   $150/$141/$149 case written out.
2. `CLAUDE.md`: module count three → four; assemblies 17 → 22; contexts 2 → 3 and the budget 8 → 12; the
   `minReplicas` paragraph inverted; the test counts refreshed from a real run, **including the skip count**.
3. `deferred-work.md`: close **C11**, **E1** and **C8** with what actually changed, not with a plan
   reference. Re-read every remaining trigger, per the file's own rule that a closing phase does so. **C6**
   and **B4/B6** stay open; update their status lines with the new counts — Task 9's fakes make B4/B6
   cheaper, and Alerts' `ConnectionStringName` makes C6 worse.
4. Update the three reference documents' "what exists today" lines. The `alerts_*` tables and four of the
   Redis keys stop being marked as arriving later.
5. Commit: `docs: record Phase 4 and close C8, C11 and E1`.
6. **Delete this file — but only after Task 18.** An implementation plan exists only while its phase is
   in flight, and a phase is in flight until it runs in a browser and is deployed.

---

### Task 17 — Verify it in a browser, locally

**This task was missing from the first draft of this plan, and its absence was a real hole**: §5 lists
browser outcomes as acceptance criteria and no task produced them. The frontend was built and unit-tested
against mocked requests, so until now nobody has opened the application and clicked the button.

**Steps.**

1. `docker compose up` from a clean volume, with a real `FINNHUB_API_KEY` absent so the fake provider runs.
2. Register, add a position, set a threshold on it.
3. **Click Simulate. The alert must appear in the panel in under a second, badged as simulated.** This is
   the phase's headline claim and the only way to check it is to watch it.
4. Reload. The alert is still listed — from history, not replay.
5. Simulate with the tab closed, reopen: the alert is in the list.
6. Leave the tab open five minutes; the connection is still alive and the badge still reads "Live (SSE)".
7. Nudge a price past a threshold; a real, evaluation-driven alert arrives with no help.
8. Nudge twice inside the cooldown — one alert. Nudge back and forth across the threshold repeatedly —
   alerts stay bounded rather than one per cycle.
9. The notifications screen lists history. **The panel is usable at 375px.**
10. Disable every threshold and confirm no `marketdata:prices:*` key is written on the next cycle.

Nothing is committed by this task unless it finds a defect. What it produces is a yes or a no.

---

### Task 18 — Deploy, and verify the deployed thing

**Also missing from the first draft.** `docs/DEPLOYING.md` is the runbook and it wins over anything here.

**Deploying is a push to `main`, and that is the whole mechanism** — `deploy.yml` fires on `push:
branches: [main]` and on `workflow_dispatch`. How the work reaches `main` is a separate decision and is
the user's: this repository has done both a squash-merged pull request and a local squash merge.

**Never run `az deployment group create` by hand.** The workflow installs Bicep and runs
`az deployment group what-if` inside the runner.

**Steps.**

1. Confirm `docs/DEPLOYING.md`'s six-step verification is still current, and read its five recorded
   failure cases before starting.
2. Get the branch onto `main` by whichever route the user chooses. **Ask; do not assume.**
3. Watch the run. The first deploy after this phase changes `minReplicas` to 1 and the scale rule to 400,
   so the container app revision is replaced rather than updated in place.
4. Verify on the deployed API, not locally: `/api/marketdata/health` still reports the real provider;
   a threshold can be set; Simulate returns 202; the alert appears in history.
5. **Open the stream against the deployed API and hold it past four minutes.** This is the one thing no
   local test can prove, because the four-minute idle close is a platform behaviour of the hosting plan.
   If the heartbeat is wrong, this is where it shows.
6. Confirm the SPA on GitHub Pages renders alerts from the deployed API across origins.
7. **Re-check the resource group's delete-by tag.** A deploy re-stamps it to today + 14 days; the last
   recorded value was 2026-08-19. An unreadable or missing tag deletes the group.

Only when this passes is Phase 4 done, and only then does Task 16 step 6 delete this file.

---

## 5. Done when

Straight from `phase-4-alerts.md` §8, in the order they are cheapest to check:

- [ ] With no alerts configured anywhere, nothing is polled and the dashboard is unchanged.
- [ ] Set a threshold, click Simulate, and the alert appears in the panel in under a second, badged.
- [ ] Reload the page and the alert is still listed — from history, not replay.
- [ ] Simulate with the tab closed, then open the app: the alert is in the list.
- [ ] Leave a tab open for five minutes and the connection is still alive.
- [ ] Nudge a price past a threshold in the local stack and a real, evaluation-driven alert fires.
- [ ] Nudge twice inside the cooldown and only one alert arrives.
- [ ] Nudge back and forth across the threshold repeatedly and alerts stay bounded rather than one per cycle.
- [ ] The notifications screen lists history; the shell badge reads "Live (SSE)"; the panel is usable at 375px.
- [ ] The alerts schema is reached by `alerts_svc`, with its own migration history table.
- [ ] Alerts arrive on the deployed site from the deployed API, and the stream survives past four minutes.
- [ ] The README records all five decisions of Task 16 step 1.
