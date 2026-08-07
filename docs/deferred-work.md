# Deferred and rejected work

The register for anything deferred, unbuilt or rejected. Each deferred item carries a **trigger** — the
event that makes it stop being deferrable. "Later" is not a trigger. Each also carries a **status** saying
whether that trigger has already happened, so an item cannot sit here forever by accident.

An item does not have to be a defect. Something described in a plan and never built belongs here too.

Rejected items are recorded with the reason, so they are not proposed again.

Items numbered `A`–`D` come from the code audit of 2026-08-02. `E` items were added later, by the work
that raised them.

When you add an item, write a trigger a later reader can check against the code. When a phase closes,
re-read the existing triggers.

---

## Deferred

### A5 — `ValidationFilter<T>` lets a request through when it finds no body to validate

`src/Shared.Api/ValidationFilter.cs:13-16` — when no argument of type `TRequest` is present it calls
`next(context)`, so validation is silently off for that route. It cannot happen today: every filtered route
declares a matching non-nullable body parameter. It would happen after a wiring mistake — a mismatched
generic argument, or a parameter renamed on one side of a refactor.

**Fix:** a `WithValidation<T>()` helper in `Shared.Api` that inspects the endpoint's `MethodInfo` metadata
when the route is mapped and throws if the delegate has no `T` parameter. A startup failure instead of a
silent bypass.

**Trigger:** Phase 2, when the route count roughly triples and the filters stop being individually obvious.

**Status: the trigger has happened, and the item stays deferred by choice.** There are **eleven** filtered
routes, counted by grepping for `AddEndpointFilter<ValidationFilter<`:

| File | Line | Request type |
|---|---|---|
| `IdentityEndpoints.cs` | 46 | `RegisterUserRequest` |
| `IdentityEndpoints.cs` | 57 | `LoginUserRequest` |
| `IdentityEndpoints.cs` | 68 | `RefreshSessionRequest` |
| `IdentityEndpoints.cs` | 107 | `SaveAppearanceRequest` |
| `PortfolioEndpoints.cs` | 55 | `AddHoldingRequest` |
| `PortfolioEndpoints.cs` | 65 | `UpdateHoldingRequest` |
| `PortfolioEndpoints.cs` | 109 | `SaveDashboardSettingsRequest` |
| `MarketDataEndpoints.cs` | 91 | `NudgeRequest` — mapped in Development only |
| `MarketDataEndpoints.cs` | 114 | `SaveApiKeyRequest` |
| `AlertsEndpoints.cs` | 69 | `SimulateAlertRequest` |
| `AlertsEndpoints.cs` | 79 | `SaveAlertSettingRequest` |

Eleven is not "roughly triples" either, and the filters are still individually obvious. Every route added
since — the two from Alerts, and Phase 5's three settings routes — declares a matching non-nullable body
parameter like all the others, so the count grew without a new *shape* of mistake appearing. Re-grep rather
than trusting these line numbers; they move with every edit above them.

### A8 — `ApiExceptionHandler` used a different problem-details namespace from the framework — **DONE (Phase 6)**

This sat in the Skipped table. The handler set its own `Title` and `Type` on the problem document, so the
same status code could describe itself two different ways depending on which code path produced it. It was
skipped because no client reads `type` — the SPA reads `status` and `errors` — and the stated fix was to
delete both assignments and let `ProblemDetailsDefaults` fill them.

**Done, exactly that way.** `ApiExceptionHandler` now sets `Status` and nothing else, with a comment saying
why the two omissions are deliberate rather than forgotten. `Detail` is still deliberately unset, so no
exception text can reach a caller. It is recorded here rather than left as a silent deletion from the
Skipped table, because a reader who noticed the row was gone could not otherwise tell a fix from a typo.

### B4 / B6 — Portfolio's handlers have no unit tests and no fakes

Where a module has no in-memory stand-ins for the ports its handlers depend on, every handler assertion has
to go end to end through Docker — so a branch that is awkward to provoke over HTTP goes untested, and so
does the *order* in which a handler does things, which no HTTP response reveals.

**Portfolio is the last module in that state.** Its eight handlers — add, update, remove and hide a holding,
read the holdings list, read and save the dashboard settings, and build the dashboard — depend on three
ports (`IHoldingRepository`, `IDashboardSettingsRepository`, `IDashboardHoldingReader`) and none of them has
a fake. `Modules.Portfolio.UnitTests` covers entities, value objects, the dashboard calculator and the
request validators, and stops at the handler boundary.

**Fix:** a `Fakes/` directory in `tests/StockPortfolio.Modules.Portfolio.UnitTests/`, copied in shape from
the two that already exist, and handler tests over it. The merge path is the one worth writing first: adding
a ticker the user already holds must read, merge and save rather than insert, and the integration test that
covers it cannot distinguish those from the outside.

**Trigger:** any change to a Portfolio handler that is not itself covered by an integration test.

**Status: this item was written about Identity and Identity is no longer the subject.** Everything the
original named — the user repository, the refresh-token repository, the password hasher, the token issuer,
the register and current-user handlers, the email well-formedness rule — was deleted when Identity moved to
ASP.NET Core Identity. Hashing, token issuing and session validation are the framework's now and are not
this repository's to unit-test. What is left of Identity's own application layer is two preference handlers
over one port, which is a much smaller version of the same gap. **The worked example the item used to carry
cannot be performed and has not been replaced by an equivalent** — nothing in Identity has an ordering
subtlety of that kind any more.

The "hard to justify for one module" argument is gone regardless. Two `Fakes/` directories exist. Alerts'
holds five — two repositories, a cooldown store, a publisher and a price-window reader — driven by
`FakeTimeProvider` and unit-testing whole handlers with no Docker. MarketData's holds two, a repository and
a secret protector. That is the shape this item asks for, built twice, so Portfolio's version is a copy
rather than a design.

### B10 — fragile assertions and duplicated test code

Duplication, all of it in `tests/`: `fixture ?? throw` opening **20** integration classes and
`TestContext.Current.CancellationToken` written out **184** times (an `ApiTest` base class would hold both);
`ReadProblemAsync` private to `AuthenticationTests` (belongs on `Wire`); raw Npgsql plumbing in three
hand-rolled shapes (a `Sql` helper); **seven** near-identical `GlobalUsings.cs` (one `Using` item group in
`tests/Directory.Build.props`); and **two fake clocks for one job** — `TestClock` in the integration project
versus `FakeTimeProvider` in the unit projects. Delete `TestClock`; `FakeTimeProvider` behaves identically
and also drives `CreateTimer`, which the poll loop needs.

**Trigger:** Phase 2's test suite — the point where the duplication stops being two copies and becomes four.

**Status: the trigger has happened, and the duplication has roughly tripled since this was written.** Phases
2, 3 and 4 each added a test assembly; there are seven. Not done, and the numbers above are the current
count, not the original one.

**The fragile-assertion half is closed, by replacement rather than by repair.** Both examples it named have
gone: the register-request validator tests went with the hand-written Identity module, and the validator
tests that replaced them assert `ErrorCode` — `"theme.unknown"`, `"language.required"` — which is what this
item asked for. `MigrationTests` no longer pins a migration filename either; it reads the schemas and table
names out of the live database and asserts on those. Neither was done *because* of this item, so treat it as
overtaken on that half rather than as a fix to copy.

**The clock half is unchanged and is now the whole of what is left.** `TestClock` is still in
`tests/StockPortfolio.Api.IntegrationTests/Infrastructure/TestClock.cs`, and it is now **dead code** — no
test refers to it. `ApiFixture.CreateHostWithClock(TimeProvider)` is still there too and is genuinely used,
by `DataProtectionPersistenceTests`, which passes it `TimeProvider.System`. So the seam is real and the fake
behind it is not: deleting the file is a one-line change with nothing to migrate.

### C2 — JWT configuration is read and validated twice — **DONE (Phase 6)**

Two places used to read the `Jwt` section, each enforcing its own 32-byte minimum and its own issuer and
audience defaults, so changing one default made the process issue tokens it then refused.

Both readers had already gone by Phase 6: sessions are ASP.NET Core Identity bearer tokens, which are
data-protected rather than signed, so there was no signing key left to read. What remained was the
configuration itself — the `Jwt` block in both host settings files, three Bicep parameters, the container
app's secret and three environment entries, a GitHub secret in two workflows, two compose entries, an
`.env.example` section and the value `ApiFixture` supplied — all of it feeding nothing. Dead configuration
that looks alive is worse than none, so Phase 6 deleted every side of it in one change rather than unifying
two readers that no longer exist.

### C3 — two Dockerfiles duplicate 22 identical `COPY` lines

`src/Host/Dockerfile` and `src/Migrator/Dockerfile` each copy **23** `.csproj` files, and 22 of the 23 lines
are byte-identical between them including column alignment — only the host project differs. (23 =
`Shared.Kernel`, `Shared.Api`, the host, and 5 layers × 4 modules.) This has already bitten once: a
repo-wide rename left both images copying `*.Presentation.csproj`, and `dotnet build` stayed green because
those paths only exist inside the container build context.

**Fix:** one Dockerfile at the repo root with a shared restore stage and two final stages selected by
`target:` in compose. The restore layer is then computed once instead of twice.

**Trigger:** the next project rename, or a phase adding projects — whichever comes first.

**Status: the trigger has happened three times**, in Phases 2, 3 and 4, each of which added projects that had
to be hand-added to both files. Phase 4 added five at once and knew it was walking into this item. Still not
done.

### C4 — the application's Redis multiplexer was registered inside the health-check extension — **DONE (Phase 3)**

`HealthCheckExtensions` parsed the connection string and registered `IConnectionMultiplexer` as a singleton,
then registered two checks. The multiplexer is the app's Redis client; a readiness probe merely observes it.

**Done.** `src/Host/Extensions/RedisExtensions.cs` now owns the connection-string name, the blank-string
throw, `ConfigurationOptions.Parse`, `AbortOnConnectFail = false` and the singleton registration.
`AddStockPortfolioHealthChecks` lost its now-unused `IConfiguration` parameter — an unused parameter is a lie
the compiler will not flag — and `RedisHealthCheck` was unchanged, since it already took the multiplexer from
DI. The call sits immediately after `AddSingleton(TimeProvider.System)`, before any module.

One thing came out of doing it that the item had not anticipated. MarketData does not open a second Redis
connection; it **injects `IConnectionMultiplexer` and so depends on a host registration it never names**.
Delete or reorder that one line in `Program.cs` and the dashboard fails on the first request, not at
startup. That is now recorded in `CLAUDE.md` under "Where Identity is not a safe template".

### C6 — the connection-string name and the `MigrationsHistoryTable` block are duplicated

`"Identity"` is spelled in four places (`IdentityModule.ConnectionStringName`, an independent redeclaration
in `PostgresHealthCheck`, `DesignTimeFactory`, `Migrator/Program.cs`), and the `UseNpgsql` +
`MigrationsHistoryTable` block exists twice. The second is efcore#24127 written out twice: if a future
module's design-time factory omits it, four contexts share one history table.

**Fix:** `IdentityDbContextOptions.Configure(builder, connectionString)` in `.Infrastructure`; everything
else references the existing public constant.

**Trigger:** Phase 2. With three modules this becomes twelve places, and the value is almost entirely in not
stamping the pattern out three times.

**Status: the trigger has happened, the pattern was stamped out anyway, and every phase since has made it
worse exactly as forecast.** `ConnectionStringName` exists independently in `IdentityModule.cs`,
`PortfolioModule.cs`, `AlertsModule.cs`, `MarketDataModule.cs` and — as `RedisConnectionStringName` —
`RedisExtensions.cs`: five declarations of the same idea. The composition changed rather than the count.
`PostgresHealthCheck.cs` was deleted when C7 closed, because every module now contributes its own database
check from its `Add<M>Module`, and MarketData's arrived in its place when Phase 5 gave it a context. There is
a sixth spelling of each name that is not a declaration and is easy to miss: `Migrator/Program.cs` writes all
four as string keys into its in-memory configuration, so a renamed constant leaves the migrator silently
configuring nothing.

The second half is the one that matters more. The `UseNpgsql` + `MigrationsHistoryTable` block now exists
**eight** times: four modules × (module registration, design-time factory). Alerts was written by copying
Portfolio's and MarketData's by copying one of those, which is precisely the mechanism this item warns
about — each copy was correct, and a copy that had dropped the history-table call would have put four
contexts into one bookkeeping table with no error anywhere. Getting it right by copying carefully is not the
same as it being enforced.

### C7 — the `postgres` readiness check probed one of the module roles — **DONE**

A hand-written check hard-coded the `Identity` connection string and registered under the unqualified name
`postgres`. Readiness could report Healthy while another module's role could not reach the database, and the
platform kept routing to that revision. Alerts sharpened it: the poller and the evaluator run on a timer
rather than on a request, so an unreachable `alerts_svc` produced no failing HTTP call for anyone to notice.

**Done.** The hand-written check and its file are gone. Every module contributes its own from its
`Add<M>Module` using the off-the-shelf `AddDbContextCheck<T>()`, so all four database logins are probed under
four distinct names, and `HealthCheckTests` pins the names **and their count** — a module that stops
contributing one is a failing test rather than a quiet gap.

**Closing this one opened a worse one, and Phase 6 closed that too.** Readiness ran *every* registered check,
Redis among them, and every check defaults to reporting unhealthy on failure — so a cache outage took the API
out of the load balancer entirely, which is the direct opposite of "Redis stopped, the dashboard still renders
prices". The cache is now registered with a *degraded* failure status, which the framework maps to 200, and
every check carries a tag its probe selects on, so readiness answers only the question it is named after and a
check added later cannot silently join it. An integration test boots a host with Redis unreachable and asserts
readiness is **200** with the cache reported degraded.

### C8 — the Migrator invents a JWT signing key — **DONE (Phase 4)**

`src/Migrator/Program.cs` supplied `"migrator-placeholder-signing-key-unused-32b"` because
`AddIdentityModule` validated the `Jwt` section eagerly, and the migration job built the entire module —
Argon2 hasher, token issuer, five handlers — to reach one `DbContext`.

**Done.** `AddIdentityPersistence(IServiceCollection, IConfiguration)` is split out of `AddIdentityModule`,
which now composes it. `MigratedModules` calls the persistence half only, and the placeholder literal and
its two configuration entries — in `Migrator/Program.cs` and in `ApiFixture`'s `MigratorConfiguration` — are
gone. Portfolio needed no split; it validates nothing beyond its connection string. Neither did Alerts, and
that is worth knowing: it was expected to need one, but its only eager check is the connection string it
genuinely cannot run without, which is exactly what the migrator wants supplied anyway.

**The stated fix was wrong about the benefit, and the wrong version must not be repeated.** It claimed the
split "tightens the `ServiceCollection` walk". It does not. The walk filters on
`ServiceType.IsSubclassOf(typeof(DbContext))`, and nothing `AddIdentityModule` registered was ever a
`DbContext` — the options record, two repositories, the hasher, the token issuer and six closed-generic
handlers are all unrelated types. The filter had no false positive to reject before and has none now. The
collection is about eleven descriptors smaller and **the walk's result is identical.**

**What the split actually buys**, and it is the thing the trigger was written about:

- The options factory is the *argument* to `AddSingleton`, so it runs at **registration** time. Every
  migrator run was parsing, base64-decoding and length-checking a signing key before touching a database.
  That code is now unreachable from the migrator rather than merely fed a dummy value.
- The migrator's contract with a module is now "give me a connection string". No future module's eager
  validation of a runtime concern can force a new placeholder line back into `Migrator/Program.cs`, which
  is what would otherwise have happened once per module for the rest of the build.

**Honest cost.** Running against a bare `ServiceCollection` used to prove *incidentally* that
`Add<M>Module` was self-contained — that it leaned on no host-registered service. That proof now covers
only Identity's persistence half. `ApiFixture` builds the real host and would catch a missing host service
by a different route, so this narrows coverage rather than opening a hole, but it is a real change in what
the migrator seam enforces for free.

Still open and unchanged, same file: `IsSubclassOf(typeof(DbContext))` finds nothing if a module uses
`AddDbContextFactory<T>` — the service type is then `IDbContextFactory<T>`. With one module the `Count == 0`
check catches it loudly; with three it does not, and that module's migrations are silently skipped. Each
module carries a comment saying `AddDbContext`, never `AddDbContextFactory`, which is a convention rather
than a check.

### C11 — adding a module takes four edits across three files, and nothing checks them — **DONE (Phase 4)**

`Program.cs` needs `Add<M>Module`, `Add<M>Api` and `Map<M>Endpoints`, and `Migrator/Program.cs` needs its own
registration. Miss `Map<M>Endpoints` and the module builds, registers, passes every unit and architecture
test, and serves nothing. The old test compared against a hard-coded list of route names per module, so a
**fourth** module added and never mapped passed everything — nobody would have added its route names to the
list either, and the two omissions cancelled out into a green run.

**Done.** `EndpointMetadataTests` now derives the set of modules that must appear from the loaded
`StockPortfolio.Modules.*.Api` assemblies rather than from a literal list, and asserts each contributes at
least one endpoint name to the host's `EndpointDataSource`. It was closed **second in the phase, before any
Alerts endpoint existed**, so the rest of the phase was protected by it rather than judged by it afterwards.
Deliberately broken and watched go red, naming the module. `IEndpointModule` was not reintroduced; the fix is
a test, not an interface.

One thing came out of doing it that the item had not anticipated, and it decides how the test may be edited
later. **Deleting `Map<M>Endpoints` does not change the derived module count**, because `Add<M>Api` keeps
that assembly loaded — the derived set is what catches a missing `Map`. The separate assertion that the
derived count equals the number of modules is therefore the only thing that would catch a module wired
nowhere at all, and it is load-bearing: raise it when a module lands, and never soften it to "the list is
non-empty". That is the same lesson `ReferenceWalker_FindsEdgesThatDoExist` carries — a rule that can pass by
finding nothing needs a companion assertion that fails when the search finds nothing.

### D9 — Data Protection keys were not persisted — **DONE (Phase 5)**

This sat in the Skipped table because nothing used the key ring: no cookies, no bring-your-own-key, and
sessions signed from configured key material. Keys were written to the container filesystem and lost on every
revision, which mattered only the day something needed them.

**That day was Phase 5.** Bring-your-own-key encrypts a user's provider key at rest, so the key ring became
load-bearing between one revision and the next — an unreadable ring means every stored key is rubbish after a
deploy. MarketData declares the need as two ports it owns, `ISecretProtector` and `IKeyRingStore`, and the
host implements them, because `.Infrastructure` may not reference ASP.NET Core and the Data Protection
packages pull it in transitively. The keys live in `marketdata.data_protection_keys`, and
`DataProtectionPersistenceTests` is what proves a second host reads what the first one wrote.

The skip reason is therefore false in every clause, which is the failure mode this register exists to
prevent: an item parked with a good reason, and nobody re-reading the reason when the ground moved.

### D10 — compose startup ordering gaps — **DONE (Phase 6)**

Three gaps, all in `docker-compose.yml`. `redis` defined a `redis-cli ping` healthcheck that nothing waited
on, because `api` used `condition: service_started`. `api` had no healthcheck at all. And `web` waited on
`api` with `service_started`, so nginx could serve the SPA before the API was listening and the first call
from the browser came back 502. Only the database half was ordered correctly — `api` already waited on
`postgres: service_healthy` and `migrations: service_completed_successfully`.

**What kept it open for four phases was one measured fact: the runtime image ships neither curl nor wget.**
Every obvious healthcheck needs one of them, and adding a package to the runtime image for a local convenience
is a poor trade. The answer was to check what the image *does* have. `mcr.microsoft.com/dotnet/aspnet:10.0` is
Ubuntu 24.04 with bash 5.2, grep and head, and **bash's `/dev/tcp` opens a TCP socket with no external
program at all**. So `api`'s healthcheck opens the socket itself, writes a `GET /health/ready HTTP/1.1` with
`Connection: close`, and greps the status line for 200. A real HTTP check, no new package, no image growth.
The one thing to get right is the escaping: `\r\n` has to survive YAML as a backslash and an `r`, so that
`printf` inside bash is what turns it into CR LF.

All three are fixed. `api` waits on `redis: service_healthy`, `api` has that healthcheck, and `web` waits on
`api: service_healthy`. `api` keeps `restart: unless-stopped` — deliberately, because the Postgres-down
condition is that the API does *not* restart-loop, and that setting is what the check is against.

**Proven by running it, not by reading it.** From `docker compose down -v`, a `docker compose up -d --wait`
reached every service healthy in **20 seconds**, with the API answering its first probe about five seconds
after its container started. Two negative controls show the check can fail on the thing it is named after: the
same command against a closed port and against a 404 path both exit non-zero, so it is testing the readiness
route rather than merely proving bash runs.

The Redis half was the one that mattered most by the end of Phase 4. The quote poller starts with the host and
reaches for Redis on its first cycle, so `api` starting before Redis is ready stopped being only a
first-request problem — the per-cycle `try/catch` absorbed it and the next cycle recovered, which is exactly
the kind of silent, self-healing wrongness this register exists to catch before someone relies on it.

### E1 — the `alerts` schema, the `alerts_svc` role and the Alerts deployment variables have no module behind them — **DONE (Phase 4)**

Phase 2 merged the Alerts module into Portfolio and the code went with it, but the database and deployment
settings did not: `CREATE SCHEMA alerts` and the `alerts_svc` role with its grants in `db/init/`, `ALERTS_PW`
and a `ConnectionStrings__Alerts` value in compose and `.env.example`, the password secret and
connection-string parameter in the Bicep, and `ALERTS_PW` as a secret and parameter on the deploy path. One
extra role and one empty schema, owned by nothing.

**Done, and proven rather than asserted.** `AlertsDbContext` exists, registers with a `Maximum Pool Size=2`
connection string named `Alerts`, and carries its own `MigrationsHistoryTable` in the `alerts` schema. From a
clean volume — `docker compose down -v && docker compose up` — the migrator reports three contexts checked
with the initial Alerts migration applied, `/health/ready` comes up green, and `psql -U alerts_svc` reads
`alerts.alert_settings`. `SchemaIsolationTests` still passes, which is what shows `alerts_svc` reaches
`alerts` and nothing else. The variables in compose, the Bicep and the workflows now feed something.

The item said not to close it on the strength of a plan, and it was not. What made it real was the clean-clone
boot, which is also the acceptance gate — the same reason the leftovers were never deleted blind in the
first place.

**Two consequences that were not leftovers.** The connection budget moved: a pool is opened for a registered
context, and Alerts made it three. Phase 5 then gave MarketData one too, so it is four — 4 × 2 × 2 = **16** of
the tier's 35. And `alerts_svc` becoming a real role widened **C7**, which is now closed.

The fallback plan — delete the schema, the role and every reference — is no longer needed and is not recorded
here; git has it.

### E2 — ticker search was specified and unbuilt — **DONE (after Phase 3, ahead of Phase 4)**

The Phase 2 plan specified a ticker search — type a few letters, see matching companies, pick one — and it
reached no implementation task. Nothing downstream picked it up, so it was never mapped, never contracted
and never built. Nothing was broken by its absence: free text validated, the existence check rejected
symbols that do not exist, and the error message was usable. What was missing was **discovery**, on the
first screen anyone uses.

**Done.** The endpoint, the cross-module name contract, the Redis name cache, the fake provider's own
search and the suggestion box all ship. Company names now appear on the holdings and dashboard tables,
which nothing else in the system could produce.

Two things came out of building it that the item had not anticipated.

**The route is `/api/marketdata/search`, not `/api/tickers/search`.** The original spelling predates the
module that owns the provider; every other route in that module is already under `/api/marketdata/`, and
one module serving two prefixes is a seam with no reason behind it. Closing condition 1 below is written
against the endpoint, not the string.

**Suggestions are filtered to symbols the add-position form would accept.** The provider's search returns
foreign listings and longer symbols, and offering one fills the field with a value the form then rejects.
This is *not* the exact-match rule the existence check uses — fuzzy hits are still kept, including the ones
that match on a company name rather than a symbol, which is what makes the feature useful to someone who
does not know the symbol. An integration test adds every suggestion the search returns and asserts each is
accepted, so the two rules cannot drift apart.

**Closing conditions, all four met:**

1. The search endpoint returns a 200 carrying an object with `symbol == "AAPL"` for a prefix of it. ✅
2. The route appears in `EndpointMetadataTests`' expected MarketData route set, which is what stops it
   being mapped and then lost (see C11). ✅
3. The Add-position field lists suggestions and still accepts free text when the endpoint fails, so a
   provider outage degrades to the Phase 2 behaviour rather than blocking the form. ✅
4. The Phase 2 plan marks the feature delivered. ✅

### E3 — the deploy smoke step proves nothing about the alert stream

Phase 6 asked the deploy's smoke step for two assertions. **The first ships.** `/health/ready` now writes a
JSON body, so the step reads it and fails the deploy unless `postgres-identity`, `postgres-portfolio`,
`postgres-marketdata` and `postgres-alerts` each report `Healthy` **by name**, instead of the deploy passing on
one word from a status line. `redis` is printed and deliberately not required to be Healthy: it is registered
with a Degraded failure status so that a cache outage keeps the replica in rotation, and demanding Healthy in
the smoke step would re-impose the exact failure that registration removed. The overall `status` is not
asserted for the same reason — it legitimately reads `Degraded` while all four databases are fine.

**The second is not built.** Proving the alert stream produces a heartbeat within thirty seconds needs a
signed-in WebSocket, which needs an account created by hand, its two credentials held as repository secrets,
and a small Node step using the `@microsoft/signalr` package the web workspace already installs. It is item 1
on Phase 6's own cut list — the most expensive item in the phase for the least visible benefit — and the
account and the secrets do not exist.

**So what a green deploy proves is narrower than it looks: the API is reachable and its four database logins
are healthy.** It proves nothing end to end about the stream — not that the hub accepts a WebSocket upgrade
through the platform's ingress, not that the Redis backplane is wired, not that a browser would receive a
single message. A deploy that broke any of those would still go green.

**Fix:** a smoke account created once by hand, its email and password as two repository secrets, and a step
after the readiness assertion that signs in over HTTP, opens the hub at `/api/alerts/stream` with the access
token in the query string, and fails if nothing arrives within thirty seconds.

**Trigger, either of two, both checkable against the code:**

- **The repository gains credentials for a non-interactive account for any other reason** — a second smoke
  check, a browser run in CI, a seeded demo login. Read the secret names out of `.github/workflows/`. Almost
  the whole cost of this item is the hand-made account and its two secrets; once something else has paid it,
  what is left is one step.
- **Any change to the three settings that only the deployed environment can exercise** — the browser pinning
  the hub to WebSockets with the negotiate step skipped, or the host's SignalR Redis backplane wiring. That
  trio is the documented exemption from sticky sessions, nothing local can disprove it, and nothing else in
  the pipeline would notice it breaking.

**Status: neither trigger has happened.** Cut on purpose in Phase 6, not overlooked. Do not close this with a
step that registers its own account on each run — that leaves rubbish in the production database, which is
already a known cost there (`docs/DEPLOYING.md` records the `smoke-*@example.com` user that exists for that
reason), and it would not be closing this item so much as widening that one.

---

## Skipped

Not deferred — these have no driver, and acting on them would be speculative.

| ID | Item | Why skipped |
|---|---|---|
| A6 | `/logout` accepts an unbounded refresh token while `/refresh` caps at 256 | The 30 MB request body limit already caps it and the route requires authorization. Adding a validator would make the body required and break the documented "omit the body and still get 204". A length check inside `LogoutAsync` is the cheap fix if it ever matters. |
| A7 | No `UseForwardedHeaders`, though nginx and ACA both send `X-Forwarded-*` | Nothing reads the client IP or scheme. `TypedResults.Created` uses a relative path, so no wrong absolute URL is generated. Becomes real the moment rate limiting is split by IP, or anything logs a client address for audit. |
| B9 | The reflection rules see *usage*, not *declaration* — an unused `ProjectReference` is invisible | Roslyn omits an assembly reference when no type from it is used, so a forbidden `ProjectReference` that is not yet used passes rules 1, 5 and 6. A test that parses the csproj files would close it. Nobody has hit this, and `.Infrastructure` being `internal` limits the damage. |
| D8 | Naming and formatting debris | `LoggingDecorator.cs` contains no type of that name; `ProblemDetailsExtensions` holds two factories that are not extensions; stray double blank lines. Cosmetic only. |

---

## Rejected

These were investigated and found not worth doing. The reasons are recorded so they are not proposed again.

| Proposal | Reason rejected |
|---|---|
| A shared generic `ValueConverter` for `UserId` / `RefreshTokenId` | It needs to construct `TId` from a `Guid` inside an expression tree. A `static abstract` interface member cannot be called in an expression tree, and `ValueConverter` takes `Expression<Func<,>>`, not a delegate. The alternative — hand-built expression trees keyed on the string `"Value"` — trades 14 duplicated lines for reflection that fails at model-build time on a rename. Revisit only if roughly eight more id types appear, and then with a source generator. |
| A `WithStandardProblems()` endpoint extension | The statuses do not appear together uniformly: `/me` is a `GET` and correctly omits 415; `/register` omits 401 and is the only 409. Any blanket helper is wrong for at least one route, and it works against the convention that every endpoint declares every status it can emit. |
| Replacing the Migrator's `ServiceCollection` walk | The walk is correct: `AddDbContext<T>` registers `T` as the service type, so `IsSubclassOf(typeof(DbContext))` finds it regardless of accessibility, and it fails loudly at zero contexts. The realistic alternatives are worse — a `public static MigrateAsync` per module means four coordinated edits per phase, and making the contexts public breaks the layering. The real problem in that file is C8. |
| `EFCore.NamingConventions` to remove 11 `HasColumnName` calls | Adding a dependency to delete eleven explicit, self-explaining column names is a bad trade. |

---

## Checked and found correct

Recorded so the same ground is not covered again.

- **The hand-written password hasher — no longer applicable.** `PhcString` and `Argon2PasswordHasher` were
  audited here and found free of correctness defects. Both types, and the Argon2 package behind them, were
  deleted when Identity moved to ASP.NET Core Identity, which brings its own hasher. There is no Argon2
  anywhere in `src/` now. The audit is kept as a line rather than deleted so nobody re-reads a
  twelve-property verification and looks for the code it describes.
- **Middleware order in `Program.cs`**, including the claim that CORS must come before authentication so a
  401 still carries CORS headers. True, and the explicit `UseAuthentication` / `UseAuthorization` calls are
  what stop the framework inserting them *before* `UseCors` — deleting those calls silently reverses the
  order.
- **The liveness / readiness split** — `Predicate = _ => false` on liveness, and a test that boots a host
  with unreachable dependencies and asserts live=200 and ready=503. Not decorative.
- **`Maximum Pool Size=2`** on every production connection string. Connection strings are defined for five
  roles, but a pool is only opened for a context that exists, and `Program.cs` registers four — Identity's,
  Portfolio's, Alerts' and, since Phase 5, MarketData's. Four pools per replica × size 2 × `maxReplicas: 2` =
  **16** of the B1ms budget of 35; `migrator` runs as a separate job. Do not restate this figure from memory —
  count `AddDbContext` calls. It has been 8, then 12, and is now 16, so any other number in any other document
  is out of date rather than describing something else.
- **`AddProblemDetails()` and `UseStatusCodePages()` are both registered**, so the 415 and 500
  `problem+json` declarations are honest — for JSON `Accept` headers. A client sending `Accept: text/html`
  gets the plain-text fallback.
