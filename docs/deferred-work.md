# Deferred and rejected work

Findings from the audit of 2026-08-02 that were **not** actioned, and why. Each deferred item carries a
**trigger** — the event that makes it stop being deferrable. "Later" is not a trigger.

Rejected items are recorded with the reason they were rejected, so they are not re-proposed.

Items numbered `A`–`D` come from that audit. `E` items were added later, by the work that raised them.

---

## Deferred

### A5 — `ValidationFilter<T>` fails open

`src/Shared.Api/ValidationFilter.cs:18-21` — when no argument of type `TRequest` is present it calls
`next(context)`, so validation is silently off for that route. It cannot fire today: all three filtered
routes declare a matching non-nullable body parameter. It fires on a *wiring* mistake — a mismatched
generic argument, or a parameter renamed on one side of a refactor.

**Fix:** a `WithValidation<T>()` helper in `Shared.Api` that inspects the endpoint's `MethodInfo` metadata
at map time and throws if the delegate has no `T` parameter. Startup failure instead of silent bypass.

**Trigger:** Phase 2, when the route count roughly triples and the filters stop being individually obvious.

### B4 / B6 — no handler unit tests exist

There are no fakes for `IUserRepository`, `IRefreshTokenRepository`, `IPasswordHasher` or `ITokenIssuer`,
so every handler assertion has to go end-to-end through Docker. Consequences today:

- `RegisterUserCommandHandler`'s check-before-hash **ordering** is untested — move the `Hash` call above the
  `FindByEmailAsync` and the whole suite stays green, while every rejected registration pays ~40 ms of Argon2id.
- Untested branches: `IsAcceptable`'s `now < session.ExpiresAt`; `GetCurrentUserQueryHandler`'s `NotFound`;
  logout's `IsNullOrWhiteSpace` half; and the reachable `InvalidInput` arm — `ada@localhost` passes
  FluentValidation's lax `.EmailAddress()` and is then rejected by `User.IsWellFormedEmail`.

**Fix:** `tests/StockPortfolio.Modules.Identity.UnitTests/Fakes/` with in-memory repositories, a
`CountingPasswordHasher` and a deterministic token issuer. Then
`TakenEmail_ReturnsEmailAlreadyUsed_WithoutHashingThePassword` asserting `HashCallCount == 0`.

**Trigger:** the first Portfolio handler. Building the fakes for one module is hard to justify; building them
for two is not, and Phase 2 adds four handlers on its own.

### B10 — brittle assertions and test duplication

Brittle: `RegisterUserRequestValidatorTests` asserts on message *copy* (`ShouldContain("Email")`, the literal
`"12"`) — reword a message and it fails for no behavioural change; assert `ErrorCode` instead.
`MigrationTests` pins a migration *filename*; comparing against `context.Database.GetMigrations()` is
rename-proof and additionally catches a *missing* migration.

Duplication: `_fixture ?? throw` in six classes and `TestContext.Current.CancellationToken` ~20 times (an
`ApiTest` base class); `ReadProblemAsync` private to one file (belongs on `Wire`); raw Npgsql plumbing in
three hand-rolled shapes (a `Sql` helper); four near-identical `GlobalUsings.cs` (one `Using` item group in
`tests/Directory.Build.props`); and **two clock fakes for one job** — `TestClock` in the integration project
versus `FakeTimeProvider` in the unit projects. Delete `TestClock`; `FakeTimeProvider` has identical
semantics and also drives `CreateTimer`, which the Phase 3 poll loop needs.

**Trigger:** Phase 2's test suite — the point where the duplication stops being two copies and becomes four.

### C2 — JWT configuration read and validated twice

`src/Api/Extensions/AuthenticationExtensions.cs:16-54` and
`Identity.Infrastructure/Security/JwtOptions.cs:14-64` each read the `Jwt` section, each enforce the 32-byte
minimum, and each declare their own `DefaultIssuer`/`DefaultAudience`. The comment at
`AuthenticationExtensions.cs:19` acknowledges the drift risk rather than removing it. Change one default and
the process issues tokens it then refuses — a 401 with no clue attached.

**Fix:** a `JwtSettings` record in `Identity.Application`, which **both** `.Infrastructure` and `.Api` already
reference, so the layering objection does not apply. One definition of "a valid signing key".

Covered today by `AuthenticationTests` registering and then calling `/me`, which proves issuer and validator
agree on the configured path. Only the *defaults* and the key-length check are uncovered.

**Trigger:** the first time either default is changed, or auth registration moves into `Identity.Api`.

### C3 — two Dockerfiles duplicate 20 identical `COPY` lines

`src/Api/Dockerfile` and `src/Migrator/Dockerfile` copy the same 20 `.csproj` files, byte-identical including
column alignment. This has already bitten once: a repo-wide rename left both images copying
`*.Presentation.csproj`, and `dotnet build` stayed green because those paths only exist inside the build
context.

**Fix:** one Dockerfile at the repo root with a shared restore stage and two final stages selected by
`target:` in compose. The restore layer is then computed once instead of twice.

**Trigger:** the next project rename, or Phase 2 adding projects — whichever comes first.

### C4 — the application's Redis multiplexer is registered inside the health-check extension — **DONE (Phase 3)**

`HealthCheckExtensions` parsed the connection string and registered `IConnectionMultiplexer` as a singleton,
then registered two checks. The multiplexer is the app's Redis client; a readiness probe merely observes it.

**Done.** `src/Api/Extensions/RedisExtensions.cs` now owns the connection-string name, the blank-string throw,
`ConfigurationOptions.Parse`, `AbortOnConnectFail = false` and the singleton registration.
`AddStockPortfolioHealthChecks` lost its now-unused `IConfiguration` parameter — an unused parameter is a lie
the compiler will not flag — and `RedisHealthCheck` was unchanged, since it already took the multiplexer from
DI. The call sits immediately after `AddSingleton(TimeProvider.System)`, before any module.

Two things came out of doing it that the item did not anticipate. The **throw's message had to be reworded**:
it named "price windows, alert cooldowns and SSE tickets", two of which moved to Phase 4, and Phase 3's only
Redis use is `marketdata:last:*`. And the trigger's stated failure mode was the right one but for a slightly
different reason — MarketData does not open a second pool, it **injects `IConnectionMultiplexer` and thereby
depends on a host registration it never names**. Delete or reorder that one line in `Program.cs` and the
dashboard fails on the first request, not at boot. That is now recorded in `CLAUDE.md` under "Where Identity
is not a safe template".

### C6 — the connection-string name and the `MigrationsHistoryTable` block are duplicated

`"Identity"` is spelled in four places (`IdentityModule.ConnectionStringName`, an independent redeclaration in
`PostgresHealthCheck`, `DesignTimeFactory`, `Migrator/Program.cs`), and the `UseNpgsql` +
`MigrationsHistoryTable` block exists twice. The latter is efcore#24127 written out twice: if a future
module's design-time factory omits it, four contexts share one history table.

**Fix:** `IdentityDbContextOptions.Configure(builder, connectionString)` in `.Infrastructure`; everything else
references the existing public constant.

**Trigger:** Phase 2. With three modules this becomes twelve sites, and the value is almost entirely in not
stamping the pattern out three times.

### C7 — the `postgres` readiness check probes one of three roles

`src/Api/HealthChecks/PostgresHealthCheck.cs:11` hard-codes the `Identity` connection string but registers
under the unqualified name `postgres`. Once the other two modules have their own roles, readiness reports
Healthy while two of three cannot reach the database, and ACA keeps routing to that revision.

**Fix:** each module contributes its own readiness check from its `Add<M>Module`; the host only maps the
endpoints. `AddDbContextCheck<T>()` does exactly this, but note that
`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` has been removed entirely — both the
`PackageReference` and its `PackageVersion` — so it must be re-added to `Directory.Packages.props` as well as
to the consuming project.

**Trigger:** Phase 2, when the second role exists.

> **Phase 3: considered, and it does not apply.** MarketData is the module Phase 3 added and it has **no
> `DbContext`** (see `CLAUDE.md`'s stated exception), so it contributes no `AddDbContextCheck<T>()` and does
> not widen the gap this item describes. Readiness still probes one of two real roles. Recorded so the item is
> not read as having been triggered and skipped.

### C8 — the Migrator fabricates a JWT signing key

`src/Migrator/Program.cs:30-32` supplies `"migrator-placeholder-signing-key-unused-32b"` because
`AddIdentityModule` validates the `Jwt` section eagerly, and the migration job builds the entire module —
Argon2 hasher, token issuer, five handlers — to reach one `DbContext`.

**Fix:** split `AddIdentityPersistence(IServiceCollection, IConfiguration)` out of `AddIdentityModule`, which
then composes it. The migrator calls only the persistence half and the placeholder disappears. This also
tightens the `ServiceCollection` walk, since the collection then holds nothing but persistence.

**Trigger:** Phase 3, or whichever module first adds eager validation of a runtime concern — a Finnhub key, a
Polly-wrapped `HttpClient`, a `BackgroundService`. Each one otherwise adds another placeholder line here.

> **Phase 3: examined, and still deferred — the trigger was not met.** Both clauses failed, and it is worth
> saying why so this is not re-triggered on sight of the phase number. The **second clause** assumed Phase 3
> would add eager validation of a runtime concern; it added the opposite. A missing `Finnhub__ApiKey` is a
> *supported* state, `FinnhubOptions.FromConfiguration` must not throw, and `AddMarketDataModule` branches to
> the fake — validating eagerly there would take down `docker compose up`, the P0 gate. MarketData therefore
> adds **no** placeholder line to the Migrator. The **first clause** was written when Phase 3 was expected to
> bring a `DbContext`; it does not.
>
> Doing it anyway would have cost three coordinated edits — split `AddIdentityPersistence` out of
> `IdentityModule`, change `MigratedModules.cs`, change `ApiFixture`'s `MigratorConfiguration` — and bought
> nothing the phase could demonstrate.
>
> **Trigger restated for Phase 4**, which is where it genuinely fires: Alerts brings a real `DbContext` **and**
> a `BackgroundService`, so the placeholder stops being one line and the split stops being speculative.

Related, same file: `IsSubclassOf(typeof(DbContext))` at `:46` finds nothing if a module uses
`AddDbContextFactory<T>` — the service type is then `IDbContextFactory<T>`. With one module the `Count == 0`
guard catches it loudly; with two it does not, and that module's migrations are silently skipped.

### C11 — adding a module takes four edits across three files, unenforced

`Program.cs` needs `Add<M>Module`, `Add<M>Api` and `Map<M>Endpoints`, and `Migrator/Program.cs` needs its own
registration. Miss `Map<M>Endpoints` and the module builds, registers, passes every unit and architecture
test, and serves nothing.

**Fix:** an architecture test reflecting over `StockPortfolio.Modules.*.Api` assemblies asserting each exposes
a `Map<M>Endpoints` that appears in the host's `EndpointDataSource`.

Note: `IEndpointModule` was deliberately deleted and should **not** be reintroduced to solve this — the fix is
a test, not an interface.

**Trigger:** Phase 2. With one module the test asserts that one module registers itself.

### D10 — compose readiness gaps

`redis` defines a `redis-cli ping` healthcheck that nothing waits on — `api` uses
`condition: service_started`. `api` has no healthcheck, and `web` waits on it with `service_started`, so nginx
can serve the SPA before the API is listening and the first API call 502s.

**Fix:** `redis: condition: service_healthy`, and a healthcheck on `api` against `/health/ready`. Note the
`aspnet:10.0` image ships neither curl nor wget, so this needs a `HEALTHCHECK` in the Dockerfile or a
shell-based probe.

**Trigger:** before the first deploy or any demo where someone else runs `docker compose up`.

### E1 — the `alerts` schema, the `alerts_svc` role and the Alerts deployment variables outlive the module — **REOPENED**

> **Closed 2026-08-04, reopened 2026-08-05 (Phase 3).** It was closed on the grounds that "Alerts is a module
> again, so these are correct as they stand". That reasoning does not hold: **Alerts was reinstated as a
> decision, not as code.** `src/Modules/` has three folders, `ModuleBoundaryTests.cs` pins seventeen
> assemblies, and no `.csproj`, `DbContext` or connection string for Alerts exists anywhere. Every orphan this
> item tracks is therefore still an orphan, still inert, and still unowned — `db/init/01-roles.sql:57-59,84,138-145`,
> `docker-compose.yml:43,127`, `.env.example:39`, `containerapp-api.bicep:126`, `ci.yml:129`,
> `deploy.yml:202,222,314`. `module-boundaries.md:237-239` makes the same false "resolved" claim and is wrong
> for the same reason.
>
> The item is reopened rather than deleted-and-rewritten because the mistake is worth keeping visible: a
> decision recorded in the documents was mistaken for a change made in the tree, and closing an item on that
> basis is how leftovers become permanent. **Phase 3 does not clean it up either** — the trade below is
> unchanged, and this environment still has no Docker daemon to re-verify the P0 gate with. The difference is
> that it is now honestly open.
>
> **New closing condition.** Phase 4 builds Alerts. If it does, this item closes *by the orphans becoming
> owned*, which is a real resolution and checkable — an `AlertsDbContext` connecting as `alerts_svc`. If Phase
> 4 slips, the fix below is the fallback and its trigger stands. Do not close it a second time on the strength
> of a plan.

Original text follows, unchanged.

Phase 2 merged the Alerts module into Portfolio (three modules, not four — see
[plan/00-overview.md](plan/00-overview.md) §"Four modules"). The five `.csproj` files, the
`Shared.Kernel/DomainEvents/` folder and the solution entries went with it. The **database and deployment
surface did not**:

| Still carries it | What |
|---|---|
| `db/init/00-roles.sh`, `db/init/01-roles.sql` | `CREATE SCHEMA alerts`, role `alerts_svc`, its grants, revokes and `ALTER DEFAULT PRIVILEGES` |
| `docker-compose.yml`, `.env.example` | `ALERTS_PW`, and an `ConnectionStrings__Alerts` value passed to the API |
| `infra/*.bicep` | the Alerts password secret and connection-string parameter |
| `.github/workflows/*` | `ALERTS_PW` as a secret/parameter on the deploy path |

Nothing connects as `alerts_svc` — the API has no Alerts connection string in `appsettings.json` and no
context to open one — so the leftovers are inert: one extra role and one empty schema. The connection budget
is unaffected, because a pool is only created for a connection string that exists.

**Why it was not cleaned up with the module.** `docker compose up` from a clean clone is the **P0 acceptance
gate**, and `db/init/` is the exact area that has already broken it once (`docker-entrypoint-initdb.d` passes
no `-v` to psql, so a `.sql` using `:'password'` aborts init under `ON_ERROR_STOP=1`). The environment that
made the merge had no Docker daemon, so a clean-clone boot could not be re-verified. Editing init SQL and
deployment parameters blind, against the one gate that must not fail, was the worse trade.

**Fix:** delete the `alerts` schema, the `alerts_svc` role and every `ALERTS_PW` / Alerts-connection-string
reference across those four places, then boot from a clean clone — `docker compose down -v && docker compose
up` — and confirm the migrator still reports every context and `/health/ready` comes up green. Check the
Bicep with `az deployment group what-if` before deploying: a removed parameter that a workflow still passes
fails at preflight, not at runtime.

**Trigger:** the next `docker compose up` on a machine with a Docker daemon. Firm deadline is the Phase 6
README/verification pass, which is where a reviewer reading `db/init/01-roles.sql` would find a role for a
module that does not exist.

---

## Skipped

Not deferred — these have no driver, and acting on them would be speculative.

| ID | Item | Why skipped |
|---|---|---|
| A6 | `/logout` accepts an unbounded refresh token while `/refresh` caps at 256 | The 30 MB request body limit already caps it and the route requires authorization. Adding a validator would make the body required and break the documented "omit the body and still get 204". A length check inside `LogoutAsync` is the cheap fix if it ever matters. |
| A7 | No `UseForwardedHeaders`, though nginx and ACA both send `X-Forwarded-*` | Nothing reads the client IP or scheme. `TypedResults.Created` uses a relative path, so no wrong absolute URL is generated. Becomes real the moment rate limiting is partitioned by IP, or anything logs a client address for audit. |
| A8 | `ApiExceptionHandler` emits a different `type` URI namespace than `ProblemDetailsDefaults` | Two problem-details contracts for the same status, but no client keys on `type` — the SPA reads `status` and `errors`. Fix by deleting the `Title`/`Type` assignments and letting the defaults fill them. |
| B9 | Reflection rules see *usage*, not *declaration* — an unused `ProjectReference` is invisible | Roslyn omits an assembly reference when no type from it is used, so a forbidden `ProjectReference` that is not yet used passes rules 1, 5 and 6. A csproj-parsing test would close it. Nobody has hit this, and `.Infrastructure` being `internal` limits the damage. |
| D8 | Naming and formatting debris | `LoggingDecorator.cs` contains no type of that name; `ProblemDetailsExtensions` holds two non-extension factories; stray double blank lines. Cosmetic only. |
| D9 | Data Protection keys are not persisted | Nothing calls `IDataProtector` — no cookies, no BYOK, JWTs are HMAC-signed from configured key material. Keys are written to the container filesystem and lost on every revision, which matters the day anything uses them. The trap is already recorded in `CLAUDE.md`. |

---

## Rejected

These were investigated and found not worth doing. The reasons are recorded so they are not re-proposed.

| Proposal | Reason rejected |
|---|---|
| A shared generic `ValueConverter` for `UserId` / `RefreshTokenId` | Needs to construct `TId` from a `Guid` inside an expression tree. A `static abstract` interface member cannot be invoked in an expression tree, and `ValueConverter` takes `Expression<Func<,>>`, not a delegate. The alternative — hand-built expression trees keyed on the string `"Value"` — trades 14 duplicated lines for reflection that fails at model-build time on a rename. Revisit only if roughly eight more id types appear, and then with a source generator. |
| A `WithStandardProblems()` endpoint extension | The statuses do not co-occur uniformly: `/me` is a `GET` and correctly omits 415; `/register` omits 401 and is the only 409. Any blanket helper is wrong for at least one route, and it attacks the convention that every endpoint declares every status it can emit. |
| Unifying `LoggingCommandHandler` and `LoggingQueryHandler` | `ICommandHandler<,>` and `IQueryHandler<,>` are structurally identical but nominally distinct, and C# has no structural typing. A single class implementing both cannot be resolved by Scrutor's open-generic `Decorate`. The only true unification merges the two interfaces, deleting the command/query distinction the convention rests on, to save 25 lines. |
| Replacing the Migrator's `ServiceCollection` walk | The walk is correct: `AddDbContext<T>` registers `T` as the service type, so `IsSubclassOf(typeof(DbContext))` finds it regardless of accessibility, and it fails loudly at zero contexts. The realistic alternatives are worse — a `public static MigrateAsync` per module means four coordinated edits per phase, and making the contexts public breaks the layering. The real problem in that file is C8. |
| `EFCore.NamingConventions` to remove 11 `HasColumnName` calls | Adding a dependency to delete eleven explicit, self-documenting column names is a bad trade. |

---

## Checked and found correct

Recorded so the same ground is not re-covered.

- **`PhcString` and `Argon2PasswordHasher`** — no correctness defects. Twelve properties verified, including
  constant-time digest comparison via `FixedTimeEquals`, `ZeroMemory` ordered after `GetBytes` has been
  consumed, `Verify` re-deriving from the *stored* salt and parameters, the load-bearing `value[0] != '$'`
  check (without it a garbage-prefixed string parses), and the `padding == 3` rejection in the base64 decode.
  The only note is that `PhcString`'s parse bounds (1 GiB memory, t=16, p=16) are far wider than anything
  `Hash()` emits — hardening, not a defect, and it requires database write access to exploit.
- **Middleware order in `Program.cs`**, including the claim that CORS must precede authentication so a 401
  still carries CORS headers. True, and the explicit `UseAuthentication`/`UseAuthorization` calls are what
  suppress the framework's auto-insertion *before* `UseCors` — deleting them silently reverses the ordering.
- **The liveness/readiness split** — `Predicate = _ => false` on liveness, and a test that boots a host with
  unreachable dependencies and asserts live=200 / ready=503. Not decorative.
- **`Maximum Pool Size=2`** on every production connection string. The *setting* is correct everywhere; the
  **arithmetic quoted around it was wrong in four places** and was re-derived in Phase 3. Connection strings
  are defined for five roles, but a pool is only opened for a context that exists, and `Program.cs` registers
  two — Identity's and Portfolio's. Two pools per replica × size 2 × `maxReplicas: 2` = **8** of the B1ms
  budget of 35. MarketData has no `DbContext` and `alerts_svc` has no consumer (E1); `migrator` runs as a
  separate job. Do not restate this figure from memory — count `AddDbContext` calls.
- **`AddProblemDetails()` and `UseStatusCodePages()` both registered**, so the 415/500 `problem+json`
  declarations are honest — for JSON `Accept` headers. A client sending `Accept: text/html` gets the
  plain-text fallback.
