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

**Status: the trigger has happened, and the item stays deferred by choice.** There are six filtered routes:
`IdentityEndpoints.cs:53,64,75` (`RegisterUserRequest`, `LoginUserRequest`, `RefreshSessionRequest`),
`PortfolioEndpoints.cs:61,71` (`AddHoldingRequest`, `UpdateHoldingRequest`) and `MarketDataEndpoints.cs:78`
(`NudgeRequest`). Six is not "roughly triples", and the filters are still individually obvious.

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

**Status: the trigger happened in Phase 2 and this is the most overdue item in the file.** Portfolio's
handlers shipped in Phase 2 and MarketData's in Phase 3, so the "hard to justify for one module" argument is
two modules out of date. There is still no `Fakes/` directory anywhere under `tests/`, and
`tests/StockPortfolio.Modules.Identity.UnitTests/` still holds only entity, value-object, validator and
hasher tests — no handler test of any kind. The consequence named above is live: move `Hash` above
`FindByEmailAsync` in `RegisterUserCommandHandler` and every test still passes.

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

**Status: the trigger has happened.** Phase 2 and Phase 3 both added test assemblies; there are six now.
Not done.

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

**Status: not yet triggered.**

### C3 — two Dockerfiles duplicate 17 identical `COPY` lines

`src/Api/Dockerfile` and `src/Migrator/Dockerfile` each copy **18** `.csproj` files, and 17 of the 18 lines
are byte-identical between them including column alignment — only the host project differs. (18 =
`Shared.Kernel`, `Shared.Api`, the host, and 5 layers × 3 modules.) This has already bitten once: a
repo-wide rename left both images copying `*.Presentation.csproj`, and `dotnet build` stayed green because
those paths only exist inside the container build context.

**Fix:** one Dockerfile at the repo root with a shared restore stage and two final stages selected by
`target:` in compose. The restore layer is then computed once instead of twice.

**Trigger:** the next project rename, or a phase adding projects — whichever comes first.

**Status: the trigger has happened twice**, in Phase 2 and again in Phase 3, both of which added projects
that had to be hand-added to both files. Still not done.

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

**Status: the trigger has happened and the pattern was stamped out anyway.** `ConnectionStringName` now
exists independently in `IdentityModule.cs:15`, `PortfolioModule.cs:15`, `PostgresHealthCheck.cs:11` and
`RedisExtensions.cs:9`. It is fewer places than forecast only because MarketData has no `DbContext`;
Phase 4's `AlertsDbContext` brings the count back up.

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

**Status: the trigger happened in Phase 2 and the gap is live today.** `portfolio_svc` is the second real
role, and `PostgresHealthCheck.cs:11` still hard-codes `"Identity"` while registering as the unqualified
`postgres`. Portfolio's role could be unreachable and readiness would still report Healthy. MarketData did
not widen the gap, because it has no `DbContext` and so contributes no check; readiness probes one of two
real roles.

### C8 — the Migrator invents a JWT signing key

`src/Migrator/Program.cs:30-32` supplies `"migrator-placeholder-signing-key-unused-32b"` because
`AddIdentityModule` validates the `Jwt` section eagerly, and the migration job builds the entire module —
Argon2 hasher, token issuer, five handlers — to reach one `DbContext`.

**Fix:** split `AddIdentityPersistence(IServiceCollection, IConfiguration)` out of `AddIdentityModule`, which
then composes it. The migrator calls only the persistence half and the placeholder disappears. That also
tightens the `ServiceCollection` walk, since the collection then holds nothing but persistence.

**Trigger:** whichever module first adds eager validation of a runtime concern — a Finnhub key, a
Polly-wrapped `HttpClient`, a `BackgroundService`. Each one otherwise adds another placeholder line here.

**Status: not triggered yet, and Phase 3 is not what triggers it.** MarketData added the opposite of eager
validation: a missing `Finnhub__ApiKey` is a *supported* state, `FinnhubOptions.FromConfiguration` must not
throw, and `AddMarketDataModule` falls back to the fake — validating eagerly there would take down
`docker compose up`, the P0 gate. MarketData therefore adds no placeholder line to the Migrator, and it has
no `DbContext` either. Doing the split anyway would have cost three coordinated edits — split
`AddIdentityPersistence` out of `IdentityModule`, change `MigratedModules.cs`, change `ApiFixture`'s
`MigratorConfiguration` — and bought nothing demonstrable.

**Phase 4 is where it genuinely fires:** Alerts brings a real `DbContext` **and** a `BackgroundService`, so
the placeholder stops being one line and the split stops being speculative.

Related, same file: `IsSubclassOf(typeof(DbContext))` at `:46` finds nothing if a module uses
`AddDbContextFactory<T>` — the service type is then `IDbContextFactory<T>`. With one module the `Count == 0`
check catches it loudly; with two it does not, and that module's migrations are silently skipped.

### C11 — adding a module takes four edits across three files, and nothing checks them

`Program.cs` needs `Add<M>Module`, `Add<M>Api` and `Map<M>Endpoints`, and `Migrator/Program.cs` needs its own
registration. Miss `Map<M>Endpoints` and the module builds, registers, passes every unit and architecture
test, and serves nothing.

**Fix:** a test reflecting over `StockPortfolio.Modules.*.Api` assemblies asserting each exposes a
`Map<M>Endpoints` that appears in the host's `EndpointDataSource`.

Note: `IEndpointModule` was deliberately deleted and should **not** be reintroduced to solve this — the fix
is a test, not an interface.

**Trigger:** Phase 2. With one module the test asserts that one module registers itself.

**Status: the trigger has happened, and a test now covers most but not all of it.**
`tests/StockPortfolio.Api.IntegrationTests/EndpointMetadataTests.cs` asserts against the host's
`EndpointDataSource` — `EndpointDataSource_ExposesTheFiveAuthRoutes`, `…TheFivePortfolioRoutes` and
`…TheMarketDataHealthRoute` — so forgetting `Map<M>Endpoints` for one of the three existing modules now
fails a test. It lands as an integration test rather than an architecture test, which is fine.

What it does not cover is the case the item was written for. The test compares against a hard-coded list of
route names per module; it does not reflect over `StockPortfolio.Modules.*.Api` assemblies. So a **fourth**
module added and never mapped still passes everything — nobody would have added its route names to the list
either, and the two omissions cancel out into a green run.

**Restated for Phase 4:** Alerts is the fourth module and the first that would actually expose the gap.
Close this item by making the test derive the expected module set from the loaded `*.Api` assemblies rather
than from a literal list. Checkable: delete `MapAlertsEndpoints` from `Program.cs` and watch a test go red
without editing any list.

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

### E1 — the `alerts` schema, the `alerts_svc` role and the Alerts deployment variables have no module behind them

Phase 2 merged the Alerts module into Portfolio. The five `.csproj` files, the
`Shared.Kernel/DomainEvents/` folder and the solution entries went with it. The **database and deployment
settings did not**:

| Still carries it | What |
|---|---|
| `db/init/00-roles.sh`, `db/init/01-roles.sql` | `CREATE SCHEMA alerts`, role `alerts_svc`, its grants, revokes and `ALTER DEFAULT PRIVILEGES` |
| `docker-compose.yml`, `.env.example` | `ALERTS_PW`, and a `ConnectionStrings__Alerts` value passed to the API |
| `infra/*.bicep` | the Alerts password secret and connection-string parameter |
| `.github/workflows/*` | `ALERTS_PW` as a secret and parameter on the deploy path |

Alerts is a module again as a *design decision*, but no Alerts code exists: `src/Modules/` has three folders,
`ModuleBoundaryTests.cs` pins seventeen assemblies, and there is no `.csproj`, `DbContext` or connection
string for Alerts anywhere. So these settings are still unused and still unowned —
`db/init/01-roles.sql:57-59,84,138-146`, `docker-compose.yml:43,128`, `.env.example:39`,
`containerapp-api.bicep:126`, `ci.yml:129`, `deploy.yml:202,222,314`. `module-boundaries.md:237-239` claims
this is resolved and is wrong.

Nothing connects as `alerts_svc` — the API has no Alerts connection string in `appsettings.json` and no
context to open one — so the leftovers are inert: one extra role and one empty schema. The connection budget
is unaffected, because a pool is only created for a connection string that exists.

**Why it was not cleaned up with the module.** `docker compose up` from a clean clone is the **P0 acceptance
gate**, and `db/init/` is the exact area that has already broken it once (`docker-entrypoint-initdb.d` passes
no `-v` to psql, so a `.sql` using `:'password'` aborts init under `ON_ERROR_STOP=1`). The environment that
made the merge had no Docker daemon, so a clean-clone boot could not be re-checked. Editing init SQL and
deployment parameters blind, against the one gate that must not fail, was the worse trade. That is still
true today.

**How this closes.** Phase 4 builds Alerts. If it does, the item closes because the leftovers become owned,
which is real and checkable — an `AlertsDbContext` connecting as `alerts_svc`. Do not close it on the
strength of a plan; a decision recorded in a document is not a change made in the code.

**Fallback fix if Phase 4 slips:** delete the `alerts` schema, the `alerts_svc` role and every `ALERTS_PW`
and Alerts-connection-string reference across those four places, then boot from a clean clone —
`docker compose down -v && docker compose up` — and confirm the migrator still reports every context and
`/health/ready` comes up green. Check the Bicep with `az deployment group what-if` before deploying: a
removed parameter that a workflow still passes fails at preflight, not at runtime.

**Trigger for the fallback:** the next `docker compose up` on a machine with a Docker daemon. Firm deadline
is the Phase 6 README and verification pass, which is where a reviewer reading `db/init/01-roles.sql` would
find a role for a module that does not exist.

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
| Unifying `LoggingCommandHandler` and `LoggingQueryHandler` | `ICommandHandler<,>` and `IQueryHandler<,>` have the same shape but are different types, and C# does not match on shape. A single class implementing both cannot be resolved by Scrutor's open-generic `Decorate`. The only true unification merges the two interfaces, deleting the command/query distinction the convention rests on, to save 25 lines. |
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
  roles, but a pool is only opened for a context that exists, and `Program.cs` registers two — Identity's and
  Portfolio's. Two pools per replica × size 2 × `maxReplicas: 2` = **8** of the B1ms budget of 35.
  MarketData has no `DbContext` and `alerts_svc` has no consumer (E1); `migrator` runs as a separate job. Do
  not restate this figure from memory — count `AddDbContext` calls.
- **`AddProblemDetails()` and `UseStatusCodePages()` are both registered**, so the 415 and 500
  `problem+json` declarations are honest — for JSON `Accept` headers. A client sending `Accept: text/html`
  gets the plain-text fallback.
