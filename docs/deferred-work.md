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

### C4 — the application's Redis multiplexer is registered inside the health-check extension

`src/Api/Extensions/HealthCheckExtensions.cs:43-49` parses the connection string and registers
`IConnectionMultiplexer` as a singleton, then registers two checks. The multiplexer is the app's Redis client;
a readiness probe merely observes it.

**Fix:** extract `AddStockPortfolioRedis(IServiceCollection, IConfiguration)` called before the health checks.

**Trigger:** Phase 3, which needs the multiplexer for price windows, alert cooldowns and SSE tickets. If it
is still here then, MarketData either depends on a health-check registration having run or opens a second
connection pool — and both failures are silent.

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

### C8 — the Migrator fabricates a JWT signing key

`src/Migrator/Program.cs:30-32` supplies `"migrator-placeholder-signing-key-unused-32b"` because
`AddIdentityModule` validates the `Jwt` section eagerly, and the migration job builds the entire module —
Argon2 hasher, token issuer, five handlers — to reach one `DbContext`.

**Fix:** split `AddIdentityPersistence(IServiceCollection, IConfiguration)` out of `AddIdentityModule`, which
then composes it. The migrator calls only the persistence half and the placeholder disappears. This also
tightens the `ServiceCollection` walk, since the collection then holds nothing but persistence.

**Trigger:** Phase 3, or whichever module first adds eager validation of a runtime concern — a Finnhub key, a
Polly-wrapped `HttpClient`, a `BackgroundService`. Each one otherwise adds another placeholder line here.

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

### E1 — ~~the `alerts` schema, the `alerts_svc` role and the Alerts deployment variables outlive the module~~ RESOLVED

**Resolved by reinstating Alerts as a module.** `db/init/01-roles.sql`, `docker-compose.yml`, `infra/*.bicep` and both workflows were never stripped of `ALERTS_PW`, the `alerts` schema or the `alerts_svc` role — deliberately, because touching them meant re-verifying the P0 gate without Docker. With Alerts a module again they are correct as they stand, and the cleanup this item tracked is not needed. Original text below for the record.


Phase 2 merged the Alerts module into Portfolio (three modules, not four — see
[plan/00-overview.md](plan/00-overview.md) §"Three modules, not four"). The five `.csproj` files, the
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
- **`Maximum Pool Size=2`** on every production connection string — five when Alerts was a module, four since
  (three service roles plus `migrator`).
- **`AddProblemDetails()` and `UseStatusCodePages()` both registered**, so the 415/500 `problem+json`
  declarations are honest — for JSON `Accept` headers. A client sending `Accept: text/html` gets the
  plain-text fallback.
