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

`src/Shared.Api/ValidationFilter.cs:15-18` — when no argument of type `TRequest` is present it calls
`next(context)`, so validation is silently off for that route. It cannot happen today: every filtered route
declares a matching non-nullable body parameter. It would happen after a wiring mistake — a mismatched
generic argument, or a parameter renamed on one side of a refactor.

**Fix:** a `WithValidation<T>()` helper in `Shared.Api` that inspects the endpoint's `MethodInfo` metadata
when the route is mapped and throws if the delegate has no `T` parameter. A startup failure instead of a
silent bypass.

**Trigger:** Phase 2, when the route count roughly triples and the filters stop being individually obvious.

**Status: the trigger has happened, and the item stays deferred by choice.** There are eight filtered routes:
`IdentityEndpoints.cs:53,64,75` (`RegisterUserRequest`, `LoginUserRequest`, `RefreshSessionRequest`),
`PortfolioEndpoints.cs:61,71` (`AddHoldingRequest`, `UpdateHoldingRequest`), `MarketDataEndpoints.cs:95`
(`NudgeRequest`) and `AlertsEndpoints.cs:72,101` (`SimulateAlertRequest`, `SaveAlertSettingRequest`). Eight is
not "roughly triples", and the filters are still individually obvious. Alerts added the two routes without
adding a new *shape* of mistake: both declare a matching non-nullable body parameter like every other.

### B4 / B6 — no handler unit tests exist

There are no fakes for `IUserRepository`, `IRefreshTokenRepository`, `IPasswordHasher` or `ITokenIssuer`,
so every handler assertion has to go end to end through Docker. Consequences today:

- `RegisterUserCommandHandler`'s check-before-hash **ordering** is untested — move the `Hash` call above the
  `FindByEmailAsync` and the whole suite stays green, while every rejected registration pays about 40 ms of
  Argon2id.
- Untested branches: `IsAcceptable`'s `now < session.ExpiresAt`; `GetCurrentUserQueryHandler`'s `NotFound`;
  logout's `IsNullOrWhiteSpace` half; and the reachable `InvalidInput` arm — `ada@localhost` passes
  FluentValidation's lax `.EmailAddress()` and is then rejected by `User.IsWellFormedEmail`.

**Fix:** `tests/StockPortfolio.Modules.Identity.UnitTests/Fakes/` with in-memory repositories, a
`CountingPasswordHasher` and a deterministic token issuer. Then
`TakenEmail_ReturnsEmailAlreadyUsed_WithoutHashingThePassword` asserting `HashCallCount == 0`.

**Trigger:** the first Portfolio handler. Building the fakes for one module is hard to justify; building them
for two is not.

**Status: the trigger happened in Phase 2, this is still the most overdue item in the file, and Phase 4 made
it markedly cheaper rather than staler.** The gap in Identity is unchanged — every consequence listed above
is still live, and moving `Hash` above `FindByEmailAsync` in `RegisterUserCommandHandler` still leaves the
whole suite green. What changed is the cost of closing it. `tests/StockPortfolio.Modules.Alerts.UnitTests/`
now has the repository's first and only `Fakes/` directory: in-memory repositories, a cooldown store, a
publisher and a window reader, all driven by `FakeTimeProvider`, unit-testing an entire handler and its five
abstractions with no Docker. That is the shape this item asks for, built and working, so Identity's version
is now a copy rather than a design. Nothing about Identity was touched, deliberately — Phase 4 was not the
phase to do it in, but the "hard to justify" argument no longer has anything left in it.

### B10 — fragile assertions and duplicated test code

Fragile: `RegisterUserRequestValidatorTests` asserts on the wording of messages (`ShouldContain("Email")`,
the literal `"12"`) — reword a message and it fails although nothing behaved differently; assert `ErrorCode`
instead. `MigrationTests` pins a migration *filename*; comparing against `context.Database.GetMigrations()`
survives a rename and also catches a *missing* migration.

Duplication: `_fixture ?? throw` in six classes and `TestContext.Current.CancellationToken` about 20 times
(an `ApiTest` base class); `ReadProblemAsync` private to one file (belongs on `Wire`); raw Npgsql plumbing in
three hand-rolled shapes (a `Sql` helper); four near-identical `GlobalUsings.cs` (one `Using` item group in
`tests/Directory.Build.props`); and **two fake clocks doing one job** — `TestClock` in the integration
project versus `FakeTimeProvider` in the unit projects. Delete `TestClock`; `FakeTimeProvider` behaves
identically and also drives `CreateTimer`, which the Phase 4 poll loop needs.

**Trigger:** Phase 2's test suite — the point where the duplication stops being two copies and becomes four.

**Status: the trigger has happened.** Phase 2, Phase 3 and Phase 4 each added a test assembly; there are
seven now. Not done. The clock half is now decided in practice rather than in principle: Phase 4's poll loop
and heartbeat are driven by `FakeTimeProvider` throughout, including its `CreateTimer`, exactly as this item
predicted — and `TestClock` is still sitting in the integration project doing the same job worse.

### C2 — JWT configuration is read and validated twice

`src/Api/Extensions/AuthenticationExtensions.cs:16-54` and
`Identity.Infrastructure/Security/JwtOptions.cs:14-64` each read the `Jwt` section, each enforce the 32-byte
minimum, and each declare their own `DefaultIssuer` and `DefaultAudience`. The comment at
`AuthenticationExtensions.cs:19` acknowledges the risk rather than removing it. Change one default and the
process issues tokens it then refuses — a 401 with no clue attached.

**Fix:** a `JwtSettings` record in `Identity.Application`, which **both** `.Infrastructure` and `.Api`
already reference, so the layering objection does not apply. One definition of "a valid signing key".

Covered today by `AuthenticationTests` registering and then calling `/me`, which proves the issuer and the
validator agree on the configured path. Only the *defaults* and the key-length check are uncovered.

**Trigger:** the first time either default is changed, or auth registration moves into `Identity.Api`.

**Status: still not triggered.** Phase 4 split `AddIdentityPersistence` out of `AddIdentityModule` (C8),
which moved the eager signing-key check off the migrator's path but left both readers of the `Jwt` section
exactly where they were. Neither default changed.

### C3 — two Dockerfiles duplicate 22 identical `COPY` lines

`src/Api/Dockerfile` and `src/Migrator/Dockerfile` each copy **23** `.csproj` files, and 22 of the 23 lines
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

**Done.** `src/Api/Extensions/RedisExtensions.cs` now owns the connection-string name, the blank-string
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

**Status: the trigger has happened, the pattern was stamped out anyway, and Phase 4 made it worse exactly as
forecast.** `ConnectionStringName` now exists independently in `IdentityModule.cs:15`,
`PortfolioModule.cs:15`, `AlertsModule.cs:21`, `PostgresHealthCheck.cs:11` and `RedisExtensions.cs:9` — five
declarations of the same idea, up from four.

The second half is the one that matters more. The `UseNpgsql` + `MigrationsHistoryTable` block now exists
**six** times: three modules × (module registration, design-time factory). Alerts was written by copying
Portfolio's, which is precisely the mechanism this item warns about — the copy was correct, and a copy that
had dropped the history-table call would have put four contexts into one bookkeeping table with no error
anywhere. Getting it right by copying carefully is not the same as it being enforced.

### C7 — the `postgres` readiness check probes one of three roles

`src/Api/HealthChecks/PostgresHealthCheck.cs:11` hard-codes the `Identity` connection string but registers
under the unqualified name `postgres`. Once the other modules have their own roles, readiness reports
Healthy while some of them cannot reach the database, and ACA keeps routing to that revision.

**Fix:** each module contributes its own readiness check from its `Add<M>Module`; the host only maps the
endpoints. `AddDbContextCheck<T>()` does exactly this. Note that
`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` has been removed entirely — both the
`PackageReference` and its `PackageVersion` — so it must be re-added to `Directory.Packages.props` as well
as to the consuming project.

**Trigger:** Phase 2, when the second role exists.

**Status: the trigger happened in Phase 2, the gap is live, and Phase 4 widened it.** `PostgresHealthCheck.cs:11`
still hard-codes `"Identity"` while registering as the unqualified `postgres`. There are now **three** real
roles — `identity_svc`, `portfolio_svc` and `alerts_svc` — and readiness probes one of them. Either of the
other two could be unreachable while the probe reports Healthy and ACA keeps routing to the revision.
MarketData still contributes no check, correctly, because it has no `DbContext`.

Alerts sharpens it in a second way: the poller and the evaluator run on a timer rather than on a request, so
an unreachable `alerts_svc` produces no failing HTTP call for anyone to notice. It is the first module whose
database being down is invisible from the outside.

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

### D10 — compose startup ordering gaps

`redis` defines a `redis-cli ping` healthcheck that nothing waits on — `api` uses
`condition: service_started`. `api` has no healthcheck, and `web` waits on it with `service_started`, so
nginx can serve the SPA before the API is listening and the first API call returns 502.

**Fix:** `redis: condition: service_healthy`, and a healthcheck on `api` against `/health/ready`. Note that
the `aspnet:10.0` image ships neither curl nor wget, so this needs a `HEALTHCHECK` in the Dockerfile or a
shell-based probe.

**Trigger:** before the first deploy, or any demo where someone else runs `docker compose up`.

**Status: the trigger has happened and passed.** The first deploy was 2026-08-02 and Phase 3 deployed on
2026-08-05; this was not done before either. Every clause above is still true of `docker-compose.yml`:
`redis` has its `redis-cli ping` healthcheck (`:73`) and `api` still waits on it with
`condition: service_started` (`:120`); `api` still has no `healthcheck` block; `web` still waits on `api`
with `service_started` (`:162`). The database half *is* ordered correctly — `api` waits on
`postgres: service_healthy` and `migrations: service_completed_successfully` — so only the Redis and SPA
halves are not. `web` has its own healthcheck (`:165`), which makes the missing one on `api` easy to misread
as present.

Re-read at the end of Phase 4: the Redis half is now the more interesting one. The poller starts with the
host and reaches for Redis on its first cycle, so `api` starting before Redis is ready is no longer only a
first-request problem. The per-cycle `try/catch` absorbs it and the next cycle succeeds, which is why this is
still deferred rather than promoted.

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

**Two consequences that were not leftovers and are now live.** The connection budget moves: a pool is opened
for a registered context, and there are now three, so the ceiling is 3 × 2 × 2 = **12** of the tier's 35. And
`alerts_svc` becoming a real role widens **C7** — the readiness probe still checks one connection string of
three.

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

---

## Skipped

Not deferred — these have no driver, and acting on them would be speculative.

| ID | Item | Why skipped |
|---|---|---|
| A6 | `/logout` accepts an unbounded refresh token while `/refresh` caps at 256 | The 30 MB request body limit already caps it and the route requires authorization. Adding a validator would make the body required and break the documented "omit the body and still get 204". A length check inside `LogoutAsync` is the cheap fix if it ever matters. |
| A7 | No `UseForwardedHeaders`, though nginx and ACA both send `X-Forwarded-*` | Nothing reads the client IP or scheme. `TypedResults.Created` uses a relative path, so no wrong absolute URL is generated. Becomes real the moment rate limiting is split by IP, or anything logs a client address for audit. |
| A8 | `ApiExceptionHandler` emits a different `type` URI namespace than `ProblemDetailsDefaults` | Two problem-details contracts for the same status, but no client reads `type` — the SPA reads `status` and `errors`. Fix by deleting the `Title` and `Type` assignments and letting the defaults fill them. |
| B9 | The reflection rules see *usage*, not *declaration* — an unused `ProjectReference` is invisible | Roslyn omits an assembly reference when no type from it is used, so a forbidden `ProjectReference` that is not yet used passes rules 1, 5 and 6. A test that parses the csproj files would close it. Nobody has hit this, and `.Infrastructure` being `internal` limits the damage. |
| D8 | Naming and formatting debris | `LoggingDecorator.cs` contains no type of that name; `ProblemDetailsExtensions` holds two factories that are not extensions; stray double blank lines. Cosmetic only. |
| D9 | Data Protection keys are not persisted | Nothing calls `IDataProtector` — no cookies, no BYOK, JWTs are HMAC-signed from configured key material. Keys are written to the container filesystem and lost on every revision, which matters the day anything uses them. The trap is already recorded in `CLAUDE.md`. |

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

- **`PhcString` and `Argon2PasswordHasher`** — no correctness defects. Twelve properties verified, including
  constant-time digest comparison via `FixedTimeEquals`, `ZeroMemory` ordered after `GetBytes` has been
  consumed, `Verify` re-deriving from the *stored* salt and parameters, the `value[0] != '$'` check that
  everything else depends on (without it a garbage-prefixed string parses), and the `padding == 3` rejection
  in the base64 decode. The only note is that `PhcString`'s parse bounds (1 GiB memory, t=16, p=16) are far
  wider than anything `Hash()` emits — hardening, not a defect, and exploiting it requires database write
  access.
- **Middleware order in `Program.cs`**, including the claim that CORS must come before authentication so a
  401 still carries CORS headers. True, and the explicit `UseAuthentication` / `UseAuthorization` calls are
  what stop the framework inserting them *before* `UseCors` — deleting those calls silently reverses the
  order.
- **The liveness / readiness split** — `Predicate = _ => false` on liveness, and a test that boots a host
  with unreachable dependencies and asserts live=200 and ready=503. Not decorative.
- **`Maximum Pool Size=2`** on every production connection string. Connection strings are defined for five
  roles, but a pool is only opened for a context that exists, and `Program.cs` registers three — Identity's,
  Portfolio's and Alerts'. Three pools per replica × size 2 × `maxReplicas: 2` = **12** of the B1ms budget of
  35. MarketData has no `DbContext` and opens no pool; `migrator` runs as a separate job. Do not restate this
  figure from memory — count `AddDbContext` calls. It was 8 through Phase 3 and that figure is now wrong
  wherever it survives.
- **`AddProblemDetails()` and `UseStatusCodePages()` are both registered**, so the 415 and 500
  `problem+json` declarations are honest — for JSON `Accept` headers. A client sending `Accept: text/html`
  gets the plain-text fallback.
