# Phase 6 — Implementation plan

Companion to [phase-6-doesnt-break.md](phase-6-doesnt-break.md). That file says *what* Phase 6 must do and
which traps to avoid. This one says *which files exist, in which project, referencing what, built in which
order* — the same relationship the phase-1 to -5 implementation plans had to their phase files. It is
deleted when Phase 6 ships.

**Goal:** stop each dependency in turn and watch the app stay usable. Provider down — last-known prices with
their age, amber, no server error. Redis down — fresh prices still render, alerts say they are suppressed.
Postgres down — 503 with a readable body and a retry screen, and the container does **not** restart-loop.
Then the same three against the deployed API.

**Architecture:** no new module, no new project. One new pure domain type, one new signal out of the poller,
one new authenticated health route, and a large amount of browser work. The four modules keep their shape.

**Tech stack:** no new NuGet package, no new npm package, no new package inside the container image. Bicep
gains a third probe.

---

## Global constraints

Copied from root `CLAUDE.md`; every task below inherits them.

- **`.Infrastructure` never references ASP.NET Core. `.Api` never references EF Core or its own
  `.Infrastructure`.** They meet only through `.Application/Abstractions`.
- **`.Infrastructure` is `internal`** except the `<Module>Module` class.
- **`.Contracts` is `public`: the ports another assembly calls, and records of primitives as their payloads.**
  No EF reference, no aggregates, no strongly-typed ids. Seven interfaces already live there —
  `IQuoteReader`, `IUserHoldsTicker` and the rest — so an eighth is the established shape, not a new one.
- **A module references only other modules' `.Contracts`.** Only `Architecture.Tests` enforces this.
- **A handler returns `OneOf<…>` directly**, mapped with `.Match`, one named lambda parameter per case.
  Inside a `.Match` arm write `Results.X(...)`, never `TypedResults.X(...)`.
- **Endpoint handlers return `Task<IResult>`**, and every endpoint declares every status it can emit. Read
  the result back from `/openapi/v1.json`, never from the source. **Make a real request before adding a
  status** — this phase adds 503 to many routes, and a `Produces` claiming a body that never arrives is
  worse than an undeclared response.
- **Money is `decimal` server-side, serialised as strings.** Percentages are computed server-side.
- **EF Core only, no raw SQL.**
- **Never add `UseResponseCompression()`.**
- **Comments: one line**, `/// <summary>…</summary>` only. No `<remarks>`.
- Frontend: **no external UI component library**. Hand-built on Tailwind, native controls.
- **`dotnet test` on the host machine fails with `0x800711C7`** (Windows Application Control). Run the suite
  in a Linux SDK container.

---

## 0. Task 0 — the documents corrected before any code — **DONE**

Three documents described a system Phases 4 and 5 had replaced, and each would have sent this phase down a
dead end. All three are corrected: the phase file (the rate allowance, the signing key, who reads the feed
signal, the second Redis connection, the walkthrough, the invalid-key condition, and the price decision itself
now that it is made), `CLAUDE.md` (the plan-folder count) and the register (C7 and D9 closed, the pool
arithmetic).

The `README.md` corrections split in two, and the split matters. **It describes the running system, so it may
only ever be brought level with code that exists.** Six claims about *today* were wrong and are fixed — the
phase number, MarketData having no database, the role count, the project count, the deleted token bucket and
the pool arithmetic — plus the readiness gap, which is restated as the live one from §2.4 rather than the one
that closed. The section on **how stale is too stale is correct for the code as it stands** and argues against
§2.1's rule. It is rewritten in **Task 13**, in the commit that makes the new rule true. Rewriting it now would
buy exactly the failure this repo has a rule against: a document confidently describing behaviour nothing
implements.

`docs/reference/module-interactions.md` was checked and needs nothing yet; what it says about each dependency
failing is accurate today, and it changes in Task 13 with the behaviour.

---

## 1. Scope

The brief's error-handling requirement, end to end, plus the register items whose written trigger is this
phase.

Phase 6 **does** build: the market clock and the dash rule; a poll heartbeat and three-state component
health; a 503 for a database that is genuinely down, with automatic retry of blips underneath it; a third
probe and real startup validation; error boundaries and the degradation UI; a reconnect that never gives up;
the compose ordering; the deploy smoke step; and the README.

Phase 6 **closes** two register items: **D10** (compose ordering, Task 8) and **C2** (`Jwt` read twice — the
subject is gone; delete it, Task 6). Two more were found already closed in code and struck in Task 0: **C7**
(readiness probing one role of four) and **D9** (Data Protection persistence, shipped in Phase 5). C7's entry
now carries the *live* gap it did not cover — a cache failure withdrawing every replica, §2.4.

Phase 6 does **not** build: alert replay; a market-holiday calendar; an in-process cache under Redis; a toast
system; rate limiting. **A5, B4/B6, B10, C3, C6 and the C8 residual stay deferred**, and Task 13 refreshes
their status lines. **A8** sits in a file Task 6 opens anyway — do it there or record that it was considered.

**Counts:** 31 projects, 22 architecture assemblies, 4 registered `DbContext`s, 16 of 35 connections. Nothing
here adds a module or a context. The health-check count in `HealthCheckTests` **rises** — see Task 7.

---

## 2. Decisions settled before any code

### 2.1 DECISION — the dash rule counts open-market minutes, not wall-clock minutes

**Kostiantyn's rule, in his words:** on Saturday or Sunday show Friday's last value; otherwise dash after one
hour.

Taken literally that has a hole on weeknights. At 03:00 on a Tuesday the last price is Monday's close, about
eleven hours old, the market has been shut the whole time, and a literal one-hour rule blanks the table of a
perfectly healthy app. Same failure the weekend clause exists to prevent, different night.

So the rule is generalised to what the weekend clause is really asking for: **count only the minutes the
market was open.** Show the last-known price when the market has been open for an hour or less since that
price was recorded.

| When | Open minutes since the last price | Result |
|---|---|---|
| Sunday 14:00, price from Friday 16:00 | 0 | **shown** — exactly what was asked for |
| Tuesday 03:00, price from Monday 16:00 | 0 | shown — the weeknight hole closed |
| Tuesday 11:00, provider down since 09:30 | 90 | **dashed** |
| Tuesday 10:15, provider down since 09:30 | 45 | shown, amber |

On Saturdays and Sundays this is identical to the literal rule. It differs only overnight on weekdays, and
only by being right. **If the literal rule is preferred, it is one predicate in one file** —
`LastKnownPrice.IsWorthShowing` — and nothing else changes.

**Where it lives.** `TradingClock`, a pure static class in `MarketData.Domain`, one method:
`OpenMinutesBetween(DateTimeOffset from, DateTimeOffset to)`. `LastKnownPrice.IsWorthShowing` calls it and
keeps its existing five-minute future tolerance. Everything downstream is unchanged: a price failing the rule
is absent from the reader's result, `DashboardCalculator` already emits an all-null row for an absent price,
and the browser already renders `—`. **The "Unavailable" state already works; this only moves when it fires.**

**Use `TimeZoneInfo`, with a two-id lookup — do not hand-roll the daylight-saving arithmetic.**
`Directory.Build.props` sets `InvariantGlobalization` to true, which removes the ICU library, which is what
lets Windows resolve a name like `America/New_York`. Measured, not assumed: in that mode the Linux container
resolves `America/New_York` and fails on `Eastern Standard Time`, and a Windows laptop does the exact
opposite. So try the first, catch `TimeZoneNotFoundException`, try the second. Three lines, and it buys real
daylight-saving rules and any future change to them. `TryConvertWindowsIdToIanaId` is **not** an option — it
is one of the calls invariant mode disables.

New York regular hours are 09:30 to 16:00, which is 14:30 to 21:00 UTC in winter and 13:30 to 20:00 UTC in
summer. Both daylight-saving switches fall on a Sunday and the session never crosses midnight UTC, so
comparing whole UTC days is safe here. Put that in the one-line comment; it is not obvious and the next
reader will otherwise re-derive it.

**Holidays are not handled, deliberately.** On Thanksgiving afternoon the table dashes an hour into a closed
market. A holiday calendar is a week of work for a demo, the failure is cosmetic and self-correcting, and
Phase 4 already rejected a market calendar for the same reason. It goes in the README's known limits.

### 2.2 DECISION — the poller writes a heartbeat, and only the health detail reads it

Nothing records that a poll cycle finished. The health panel needs it; the Alerts evaluator does not, because
it already refuses to fire on a window that straddles a silence, per ticker, which beats one global number.

- `QuotePoller` writes `marketdata:poll:last` at the **end** of a successful cycle: the timestamp, how many
  tickers it was asked for, and how many it stored.
- `MarketData.Contracts` gains `IFeedHealth` returning a `FeedHealth` record of primitives.
- `MarketData.Infrastructure` implements it from that one key.
- A health check inside `AddMarketDataModule` turns it into a `HealthCheckResult`. No module reads another.

**Three states, from multiples of the configured poll interval** (default 60s, so 3 and 10 minutes):

| Condition | State |
|---|---|
| The last cycle had no targets | **Healthy** — nobody has an alert set, so no work is the right amount of work |
| Last cycle within 3 × interval | Healthy |
| Last cycle within 10 × interval | **Degraded** |
| Older, or never | **Unhealthy** |

The first row is the one that is easy to get wrong and impossible to notice: with no alerts configured the
poller takes its lease, gets an empty list and does nothing, and a naive "did we store any prices" check calls
a brand-new deployment broken forever.

### 2.3 DECISION — the health detail is a health-check endpoint, and it never answers 503

Two endpoints with different jobs.

`GET /api/marketdata/health` **stays exactly as it is**: anonymous, provider name only. It exists so somebody
running locally with no account can see the fake provider is on, it is pinned by `EndpointMetadataTests`, and
the README points at it.

`GET /api/health/detail` is new, **authenticated**, and is not a hand-written route. Everything it reports is
already a registered health check — four database checks contributed by the modules, Redis contributed by the
host, and the feed check added in §2.2 — so it is one more `MapHealthChecks` beside the two that exist, with a
`ResponseWriter` and `.RequireAuthorization()`. Writing it by hand would make the host the place every future
component has to touch, which is the one endpoint placement `CLAUDE.md` rejects by name.

`HealthStatus` is already the three-state value this needs. The same response writer serves `/health/ready`,
so §2.10's smoke step reads one shape, not two.

**No rate-allowance field**, because Phase 5 deleted the token bucket it would have measured. The browser's
health card has stubbed *Quota* and *Latency* rows whose translated text is literally `Phase 6`; both are
**deleted**, not filled. A number nothing measures is worse than no row.

⚠️ **A health route must never answer 503 because the database is down.** §2.5 gives every database-touching
route a 503; these two are carved out explicitly. An endpoint whose job is to report that Postgres is
unhealthy has to answer **200 with a body saying so**, or the browser's health card goes blank at the exact
moment it becomes useful — and §2.4's alerts-suppressed banner, which reads the cache entry from this body,
stops working whenever Redis and Postgres are down together.

### 2.4 DECISION — Redis down must not take the replica out of rotation

**This is the most serious thing the review found, and it is already true in the code.** Readiness runs every
registered check with no filter, and every check is registered with the default failure status of Unhealthy.
So Redis being down makes `/health/ready` answer 503, Container Apps pulls the replica out of rotation, and
with at most two replicas the whole API becomes unreachable. That is the exact inverse of this phase's own
done-condition — *Redis stopped, the dashboard still renders prices*. **The feature cannot pass its
acceptance test until this is fixed.**

Three changes:

1. Register Redis with `failureStatus: HealthStatus.Degraded`. A cache outage keeps readiness at 200, which
   is already what the framework maps Degraded to.
2. **Tag every check** — `"ready"`, `"startup"` — and give each `MapHealthChecks` call a matching predicate.
   Without tags, every check this phase adds silently joins readiness, and `/health/startup` becomes a second
   readiness probe rather than a migrations probe.
3. The test that can go red on this specific mistake: boot `CreateHostWithRedisDown` and assert `/health/ready`
   returns **200**. Not 503 — that is what it does today.

Two more Redis items, both smaller.

**The catches are too narrow.** Four stores in `MarketData.Infrastructure` and `RedisAlertCooldownStore` in
`Alerts.Infrastructure` — nine catch sites — catch `RedisException` only. A raw `TimeoutException` or an
`ObjectDisposedException` on a torn-down connection walks past them and becomes a 500, out of code whose whole
purpose is to swallow. Phase 5 fixed this same mistake once in the provider layer, where three named exception
types let an HTML error page through a 200. Same fix: `catch (Exception ex) when (ex is not OperationCanceledException)`.

**SignalR opens its own Redis connection and nothing configures it.** `AddStackExchangeRedis` gets the
connection string and sets only a channel prefix, so it never inherits the `AbortOnConnectFail = false` that
`RedisExtensions` sets on the application's own connection, and the compose string does not carry it either.
Redis down at host start therefore leaves the backplane permanently broken while everything else recovers.
Parse once in `RedisExtensions`, expose the options object, hand the same one to SignalR.

**What a user must see:** the dashboard unchanged, showing fresh prices — the counter-intuitive half, and it
belongs in the README — and the alerts panel saying alerts are suppressed. The panel learns that from the
health detail's cache entry, not from the stream, because a single replica's hub still delivers locally with
the backplane down. The README also carries the reason for the asymmetry: a stale price is a degraded read, a
made-up price history is a wrong alert, so history is never invented to keep the evaluator busy.

### 2.5 DECISION — a genuinely-down database is 503, and blips are retried underneath it

Today every unhandled exception is a flat 500 with no detail. Two changes, in this order.

**Retry the blip.** `EnableRetryOnFailure` on all four contexts — but **with explicit numbers, not the
defaults.** The default is six attempts backing off to thirty seconds, so a request against a stopped Postgres
would block for about a minute before its 503, with the browser on a spinner and the readiness probe timing
out at five seconds. Three attempts, two seconds apart. And it is not free even with no transactions: turning
it on makes EF buffer every result set, which the plan should not pretend costs nothing.

It also becomes a standing rule for `CLAUDE.md`: **any explicit transaction added later must run through the
execution strategy, and the work inside it must be safe to run twice.**

**Answer honestly when the retries run out.** `ApiExceptionHandler` gains one extra test that decides between
503 and 500. It has to be the right test, because the documented merge race deliberately answers **500** on a
unique-index violation and must keep doing so.

That test is `DbException.IsTransient` — in the base class library since .NET 6, so no Npgsql reference is
added to the host. Npgsql sets it true when a connection failure is underneath and false for a 23505 unique
violation. Check the thrown exception **itself first and then the inner chain**: a failed `SELECT` throws
`NpgsqlException` straight out with nothing wrapping it, while a failed save is wrapped by
`DbUpdateException` and an exhausted retry by `RetryLimitExceededException`.

| The chain contains | Answer |
|---|---|
| A `DbException` with `IsTransient == true` | **503** `problem+json`, with `Retry-After` |
| A `DbException` whose message is pool exhaustion | **503** — see below |
| A `DbException` with `IsTransient == false` (the merge race) | 500, unchanged |
| Anything else | 500, unchanged |

The pool row is not cosmetic. Every connection string carries `Maximum Pool Size=2`, and Npgsql reports pool
exhaustion as a bare `NpgsqlException` with no inner exception, so `IsTransient` is false — the plainest 503
there is would otherwise answer 500.

Every route that reads or writes the database gains a `.ProducesProblem(503)`, **except the two health
routes** (§2.3). Drive a real request with Postgres stopped before adding a single one of those declarations,
and read the shape back from `/openapi/v1.json`.

⚠️ **Two existing tests get slower.** `CreateHostWithUnreachableDependencies` and `CreateHostWithRedisDown`
both assert on `/health/ready`, and `AddDbContextCheck` calls `CanConnectAsync`, which now retries with
backoff. Three attempts two seconds apart is a few seconds, not a minute — which is the other reason for
capping the retry numbers.

### 2.6 DECISION — the startup probe checks migrations, and dead configuration is deleted rather than validated

`/health/startup` is a third endpoint with a third meaning: **migrations applied and configuration usable.**
It asks each of the four contexts for pending migrations and fails while any list is non-empty. That is a
database round trip, which is exactly why it must never be the liveness probe. It is tagged `"startup"` so it
runs that check and nothing else.

In Bicep it needs a generous budget, and **the obvious numbers are rejected at deployment validation**:
Container Apps caps `failureThreshold` at **10** and `initialDelaySeconds` at 60. Ten failures at
thirty-second periods is the same five minutes and is inside the limits. `successThreshold` must stay 1 for a
startup probe. Liveness and readiness keep their current settings untouched.

The phase file's other startup demand — a signing key present and long enough — has no subject: nothing reads
the `Jwt` section. Validating dead configuration is worse than not validating it, because it makes the dead
thing look alive. **Delete it**, and confirm by grep before deleting anything.

⚠️ **The deletion list is longer than it looks, and half of it lives on the deploy path.** The block in
`appsettings.json`, the three Bicep parameters, the parameters written into the deploy job's parameter file,
the `JWT_SIGNING_KEY` input in the workflows, the compose environment entries, `.env.example`, and the value
`ApiFixture` supplies. Miss one side and the Task 14 deploy fails on an undeclared parameter, eight tasks
after the change that caused it. Move both sides in one commit.

### 2.7 DECISION — the browser never gives up, and it knows when it is offline

`useAlertStream` passes a six-element delay array to `withAutomaticReconnect`. SignalR appends a null to that
array and stops the moment it reaches it, fires `onclose`, and the hook goes to offline. Nothing restarts it.
**After about forty-eight seconds of trouble the app is offline until the page is reloaded**, and the badge
reports it calmly and permanently.

Replace the array with a retry-policy object whose `nextRetryDelayInMilliseconds` never returns null: 0, 1, 2,
5, 10, then 30 seconds for ever.

⚠️ **That alone is not enough, and the gap is easy to miss.** Automatic reconnection only covers a connection
that succeeded once. A failed **first** `start()` — reload the page while the API is down — never enters the
policy at all, and `onclose` does not even fire, because SignalR skips it when the connection never started.
So Task 11 also needs its own retry loop around `start()`.

On top of both, the browser's own online state: pause while `navigator.onLine` is false, force an attempt on
the `online` event. Nothing in the SignalR client reads it, so this is genuinely missing rather than
duplicated. The phase file's thin reconnecting bar is driven by the pair.

### 2.8 DECISION — error boundaries: one at the root, one per authenticated route, one inside the dashboard

One of nine route files has an error component today, and there is no React error boundary anywhere. A throw
while rendering the alerts panel takes the dashboard down with it, which is one of the phase's stated
done-conditions.

- A default error component on the router covers everything with none of its own.
- `dashboard`, `settings` and `notifications` each get one, matching `portfolio`'s: the shell stays, the
  message is inline, the retry resets the query rather than reloading the page.
- **The alerts panel gets its own boundary inside the dashboard route**, because "a crash in the alerts panel
  must not take the dashboard down" is not satisfied by a route boundary — a route boundary replaces the
  route. This is the difference between the done-condition passing and appearing to pass.

**A failed dashboard *fetch* is not a render throw and must not reach any of these.** It keeps the last good
table with an inline banner, which is what the dashboard already does and what a test already pins. A 503
changes the banner's wording, not the behaviour. The full-page retry screen the phase file describes for
Postgres-down is what the route boundary shows when the route has no data at all to keep.

### 2.9 DECISION — keep the stale threshold, name the reason, mark the last-known price

Twice the refresh interval, as chosen: 20 seconds at the 10-second setting, 2 minutes at the 60-second
default, 10 minutes at 300. `Freshness` already does exactly this, so the threshold is a decision to **keep**.

Three things are missing around it:

- The amber banner names no reason. It must say the quote provider is not responding.
- A last-known price and a merely-late price look identical, and a last-known price with no timestamp shows
  no marker at all. The price cell names a last-known price as one.
- The unavailable state needs its explicit note — the table keeps its structure, the price column shows a
  dash, totals show cost only, and a line says prices are unavailable. **Never `$0.00`**, which a test already
  pins and which must stay pinned.

### 2.10 DECISION — the walkthrough is the README's, and the smoke step grows two assertions

The done-list points at a walkthrough section in the overview that does not exist. The only checkable
procedure in the repo is `README.md`'s *Checking the whole thing by hand*, which already carries the
provider-down case. **That is the walkthrough**, and its stale closing line about Phase 5 not being built goes.
It also gains the three `docker compose stop`/`start` commands the phase asks be scripted.

The deploy already smoke-tests `/health/ready` for a 200. Two more:

- `/health/ready` gets the same JSON response writer as the detail route, so the existing anonymous step can
  assert each database check by name instead of reading one word. No secret needed. Note plainly that this is
  the readiness body and not the authenticated detail the phase file names — the two now share a shape, and
  the difference is only which checks each runs.
- The stream heartbeat needs a signed-in WebSocket, so it needs an account: one created by hand, its
  credentials in two repository secrets, and a small Node step using the `@microsoft/signalr` package the web
  workspace already installs. This is the most expensive item in the phase for the least visible benefit, and
  it is **the first thing cut** — see §7.

### 2.11 DECISION — an invalid provider key is reported, not swapped away

The done-list says an invalid key plus a restart should leave the app running on the fake provider. It does
not do that today — the provider is chosen on whether a key string is non-empty, and an invalid key is
non-empty — and **it should not**. Falling back to generated prices because a key was rejected would serve
invented numbers for real tickers on the deployed site, which is the one thing `CLAUDE.md` says the fake
provider must never do.

So the app starts, as it already does, and the failure is made visible instead:

- The first 401 or 403 from the provider raises a one-shot rejected-key flag.
- The feed health check reads it and reports **Unhealthy**, with the reason.
- The dashboard falls back to last-known prices exactly as it does for any other outage.

The phase file's done bullet is corrected in Task 0 to match: *starts, warns, keeps serving last-known prices,
and the health panel says the key was rejected.*

---

## 3. Build order

Each task ends with a green build and a green suite. Run the suite in a Linux SDK container.

### Task 0 — the document corrections in §0

### Task 1 — green the four failing integration tests — **DONE**

Measured, not assumed: **723 passed, 2 skipped, 0 failed of 725**, from one run in the Linux SDK container
with Docker up, and **65 passing across 13 files** in the browser. The integration assembly went from 218 to
222, which is exactly the four that were failing.

The bar for every later task is that this stays true.

### Task 2 — `TradingClock`, the dash rule, and the fixture clock that hides it

`MarketData.Domain/TradingClock.cs` with the two-id time-zone lookup. `LastKnownPrice.IsWorthShowing` calls it.

⚠️ **The shared unit fixture clock is 12:00 UTC, which is before the 13:30 UTC summer open.** Leave it there
and every age case in the MarketData unit suite computes zero open minutes, the suite goes green, and the rule
this task exists to build is never exercised by anything. Move it into the session — 15:00 UTC — as part of
this task.

One existing test goes red and it is not an integration test: `LastKnownPriceTests.LastKnown_FiveDayOldPrice_IsStillShown`
asserts the decision this task reverses. Rewrite it under the new name rather than adjusting the number. The
four integration tests that serve a last-known price all write the price seconds before reading it, so they
compute zero open minutes and are unaffected — which is also why none of them can tell the new rule from the
old one.

⚠️ **If an integration test ever freezes the clock to a Sunday, it must sign in on that same fixed-clock host.**
`CreateHostWithClock` replaces the whole host's `TimeProvider`, including bearer-token validity, so a token
minted elsewhere at real-now is rejected or mis-aged. This is also the moment register item B10 stops being
theoretical: the integration project's own `TestClock` and the unit projects' `FakeTimeProvider` are two clocks
doing one job.

### Task 3 — widen the Redis catches, fix SignalR's connection

Four stores in `MarketData.Infrastructure` plus `RedisAlertCooldownStore` in `Alerts.Infrastructure`, nine
catch sites. `RedisExtensions` exposes its parsed options; `SignalRExtensions` takes the same object.

### Task 4 — the poll heartbeat and `IFeedHealth`

`QuotePoller` writes the key at the end of a successful cycle. `MarketData.Contracts/IFeedHealth.cs` and its
`FeedHealth` record; the implementation in `MarketData.Infrastructure`; the rejected-key flag from §2.11; the
three-state rule as a pure function so it is unit-testable against `FakeTimeProvider` with no Redis.

### Task 5 — small backend honesty fixes

Three, all cheap, all named by the phase file and all missing today:

- `QuoteReader`'s bring-your-own-key read and the decrypt behind it are unguarded, so a corrupt key ring or a
  database blip aborts the whole dashboard for a user who saved a key. Fall back to the application's key.
- A rate-limited fetch logs one warning **per symbol** — twenty on a twenty-holding dashboard. Collect the
  failures out of the parallel loop and log once with a count.
- A response that is not the shape expected is logged as a warning with no body. Log the raw body at debug
  level, which is what makes an HTML error page from a proxy diagnosable at all.

### Task 6 — 503 for a transient database failure

`EnableRetryOnFailure(3, 2s)` on the four contexts. The chain walk in `ApiExceptionHandler`, starting at the
exception itself, with the pool-exhaustion arm. `A8` is in this file — fix it or record the decision. Delete
the dead `Jwt` configuration, **both sides in one commit** (§2.6). Add `.ProducesProblem(503)` to every
database-touching route except the two health routes, after driving a real request with Postgres stopped.

### Task 7 — health: tags, three states, three probes

Redis registered as Degraded. Tags on every check and a predicate on every map. `/health/ready` gains the JSON
response writer. `/health/startup` is added. `GET /api/health/detail` is added as a third `MapHealthChecks`
with `.RequireAuthorization()`. While in `HealthCheckExtensions`, delete the `<remarks>` block that breaks the
comment rule.

`HealthCheckTests` pins the check names **and their count**; both rise. Raise them — never soften the count to
"non-empty". Check that `EndpointMetadataTests`' derived module count is unmoved, since this route belongs to
the host and not to a module.

### Task 8 — infrastructure: the third probe, and compose (closes D10)

Bicep gets the `Startup` probe at ten failures on thirty-second periods (§2.6).

Compose gets an `api` healthcheck, `redis` waited on with `service_healthy`, and `web` waiting on `api`
healthy. **Install nothing to do it.** The runtime image is Ubuntu with neither curl nor wget, which is why
D10 sat open for four phases — but it has bash with `/dev/tcp`, so the healthcheck can open a socket, write a
`GET /health/ready`, and grep the status line for 200. A real HTTP check, no new package, no image growth.

Also confirm `restart: unless-stopped` is on `api` — the Postgres-down done-condition is that the API does
*not* restart-loop, and that setting is what the check is against.

### Task 9 — error boundaries

Router default, three route components, the alerts-panel boundary inside the dashboard route.

### Task 10 — the degradation UI

The reason on the amber banner; the last-known marker and the unavailable note in the price cell; the
alerts-suppressed banner from the health detail's cache entry; the health card rewired to the detail endpoint,
**given its own `refetchInterval`** — it has none today, so it refreshes only when the route remounts — and its
two stubbed rows deleted; the dashboard's refresh-interval mutation surfacing its failure instead of silently
snapping back; and the delete and visibility mutations keeping the server's message instead of replacing it
with a generic one.

New keys in `en` **and** `uk` in the same commit. There is no English fallback, so a missing Ukrainian key
renders as the raw key string.

### Task 11 — the reconnect that never gives up

The retry-policy object, **the retry loop around the first `start()`** (§2.7), the online and offline
listeners, the reconnecting bar.

### Task 12 — the deploy smoke step

The per-check assertion from the readiness body. Then, if not cut, the smoke account and the heartbeat step.

### Task 13 — the rest of the documents

§0 handled the ones that would mislead this phase. This task does the ones that describe its results: the
README rewrite (§5), the `CLAUDE.md` rules and counts, and the register status lines that are still deferred.

### Task 14 — verify by hand, deploy, verify again

§6. ⚠️ Two of its cases cannot both be provoked in one sitting — see the note there.

---

## 4. Tests — and what each one must be able to fail on

A test that cannot fail on the mistake it is named after is what this repo has been bitten by three times. The
question is not "can it go red?" but "can it go red on *that*?"

| Test | Must go red when |
|---|---|
| `TradingClock` | The daylight-saving mapping is inverted, or the session runs to 21:00 UTC in summer. Pin both switch dates. **Every case must use a clock inside the session** — outside it, every assertion is vacuous |
| `LastKnownPrice`, three cases | A Sunday price is dashed; a Tuesday mid-session price 90 minutes old is served; a Tuesday 03:00 price from Monday's close is dashed. One case each way plus the overnight one; a one-sided test passes for a rule that always says yes |
| Redis store catches | A `TimeoutException` escapes. Not a `RedisException` — that passes today, so a test using one proves nothing about this change |
| **`/health/ready` with Redis down** | It returns 503. This is the live defect in §2.4 and this is the only assertion that catches it |
| Feed health | A cycle with **zero targets** reports anything but Healthy — the case a naive version gets wrong |
| Rejected key | The feed reports Healthy while every quote is coming back 401 |
| 503 mapping | A unique-index violation returns 503. The merge race must stay 500 |
| Health detail | It answers 503 when the database is down, instead of 200 with a body saying so |
| Health detail | Any component reports two states where three are possible. Assert the **Degraded** value explicitly, driven by `FakeTimeProvider` |
| Startup probe | It reports ready while a migration is pending, or it runs the Redis check |
| Alerts-suppressed banner | The panel renders empty rather than saying suppressed, with the cache unhealthy |
| Reconnect | It stops retrying after the sixth attempt — drive seven failures, six pass today — **and** a failed first `start()` never retries |
| Alerts-panel boundary | A throw in the panel unmounts the dashboard |

Existing coverage that must not be weakened: `Dashboard_ProviderReturns429_Returns200NotError` asserts
`IsLastKnown == false` on served symbols, and that assertion is the only thing separating the right
implementation from the one Phase 3 rejected. `Health_Live_IgnoresDependencies` is the liveness split.

The fixture already has what this phase needs and does not need a container stopped mid-test:
`CreateHostWithRedisDown`, `CreateHostWithUnreachableDependencies`, `CreateHostWithQuoteProvider`,
`CreateHostWithClock` and `ScriptedQuoteProvider`. The fixture's own comment explains why stopping a shared
container is the wrong tool, and this phase does not overturn it.

---

## 5. The README

The done-list asks for roughly a page plus a link. Present already: the one-command run, the transport
comparison table, the fake provider, bring-your-own-key, the cost figure, and the provider's inferred rate
ceiling.

**Missing:**

- A generated SQL statement with its placeholders beside the parameter values. The claim is made and never
  shown; the material is already in the parameterisation test and the recording interceptor.
- A trimmed "what we rejected, and why" table. The source is the register's Rejected section.
- Known limits: the ticker ceiling, the browser's six-connections-per-origin cap, and the holiday gap (§2.1).
- How to tear the deployment down. Only the passive "a workflow deletes it" is written.
- The three `docker compose stop`/`start` commands, in the hand-verification section.
- Why Redis down changes nothing on the dashboard but suppresses alerts: a stale price is a degraded read, a
  made-up price history is a wrong alert.

**The section on how stale is too stale.** It currently argues that age must never disqualify a price, which
is correct for the code as it stands and wrong the moment §2.1 lands. Rewrite it here, in the commit that
makes the new rule true — not before. The new version has to carry the counter-argument the old one makes,
because it is a good one: a dash *does* recreate the blank table the fallback exists to prevent, and the
answer is that it can now only happen after an hour of open market with nothing to show, which is a genuinely
broken state rather than a healthy Sunday.

The six claims that were wrong about *today* — the phase number, MarketData having no database, the role
count, the project count, the deleted token bucket and the pool arithmetic — were fixed in Task 0.

---

## 6. Done when

- Redis stopped — the dashboard still renders prices, **the API stays in rotation**, the panel says alerts are
  suppressed. Started again — it recovers within a cycle and the banner clears.
- An invalid provider key plus a restart — the app **starts**, logs a warning, keeps serving last-known
  prices, and the health panel says the key was rejected (§2.11).
- Provider blocked mid-session — amber within twice the refresh interval, last good numbers kept, no server
  error in devtools.
- Postgres stopped — 503 with a readable message and a retry button, arriving in seconds not a minute, and the
  API **not** restart-looping. Watch `docker compose ps`, not the browser; a restart loop looks fine for the
  first few seconds.
- An error forced inside the alerts panel — the dashboard stays up.
- A failed position mutation — the row reverts and **the server's own message** is inline on the form. Provoke
  it with the provider actually down; an optimistic rollback written wrongly only bites when a mutation
  genuinely fails, and nothing else in the suite will tell you.
- Offline, then back online — the bar appears and clears, and the gap fills with no manual refresh. Then
  offline for **five minutes**, and reload the page while the API is down — both still reconnect, which
  neither does today.
- Both suites green, including the four that are red today.
- Deployed, and the provider-down case repeated against the live API.
- A clean-clone startup brings up four schemas and four logins, each actually connected by something.
- The README's *Checking the whole thing by hand* passes end to end, locally and deployed.

⚠️ **Two of the dash-rule cases cannot both be checked by hand in one sitting.** "Blocked into the next
trading hour, so the column dashes" needs an hour of open US market with the provider blocked; "blocked over a
weekend, so Friday's numbers stay" needs a weekend. Either add an environment override for the session window
so both can be provoked in minutes, or state plainly that these two are proven by unit test and not by hand.
Do not leave them on the list looking checked.

---

## 7. If time runs out

The phase file's cut order, made specific. Cut in the order listed — **item 1 goes first.**

1. **The alert-stream heartbeat in the deploy smoke step** (§2.10). It needs a hand-made account and two
   secrets for the least visible benefit in the phase. The per-check readiness assertion in the same task is
   cheap and stays. This is new work this plan invented, so cutting it first leaves Kostiantyn's own three
   priorities in their stated order.
2. **Polish on the bring-your-own-key screen.**
3. **The Postgres-down path** (Task 6). A reviewer is far less likely to stop a database than to leave a bad
   key in their environment file.
4. **The responsive re-pass.**

**Never cut the provider-down path.** It is the one a reviewer will actually trigger, by leaving a bad key in
`.env` — and with §2.1 and §2.11 in place it also carries the most new behaviour.

Note that Phase 6 is the last phase, so nothing can overrun *into* it. The pressure here is the deployment's
`deleteAfter` deadline, not a spillover from Phase 5.
