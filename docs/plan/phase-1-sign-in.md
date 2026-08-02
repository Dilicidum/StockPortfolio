# Phase 1 — Sign in · 1.25 days

## 1. Goal

Register and log in on a **public Azure URL**, and locally via `docker compose up`. Hard-refresh the browser and stay signed in. Log out and watch a protected route bounce to login.

This phase carries **all** the infrastructure. Every later phase adds only a delta. Infrastructure discovered on day 6 is infrastructure that sinks you.

Covers P0 req 1 (auth incl. session persistence), the auth half of req 3 (routing), and req 7 (compose).

---

## 2. Backend

### 2.1 Shared.Kernel

```csharp
public abstract class AggregateRoot<TId> where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    [NotMapped]                                        // ← or EF tries to map it and model building throws
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent e) => _domainEvents.Add(e);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

public interface IDomainEvent { DateTimeOffset OccurredAt { get; } }

public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Usd(decimal amount) => new(amount, "USD");
    public Money Add(Money other) => Currency == other.Currency
        ? this with { Amount = Amount + other.Amount }
        : throw new InvalidOperationException($"Cannot add {other.Currency} to {Currency}");
}
```

CQRS contracts:

```csharp
public interface ICommandHandler<in TCommand, TResult> {
    Task<TResult> Handle(TCommand command, CancellationToken ct);
}
public interface IQueryHandler<in TQuery, TResult> {
    Task<TResult> Handle(TQuery query, CancellationToken ct);
}
```

Endpoint registration, so each module contributes its own group:

```csharp
public interface IEndpointModule { void MapEndpoints(IEndpointRouteBuilder app); }
```

Registered manually in `Api/Program.cs` — one line per module. Assembly scanning works but is not trim-safe and hides ordering.

### 2.2 Decorators (the reason CQRS earns its keep without a dispatcher)

```csharp
internal sealed class ValidationDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResult> { … }

internal sealed class LoggingDecorator<TCommand, TResult> …
```

Register with explicit `AddScoped` + `Decorate` per handler. When a handler is injected into an endpoint, DI hands it the decorated chain — no mediator involved.

⚠️ If you later enable `EnableRetryOnFailure`, a transaction decorator **must** wrap its work in `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`, and the handler must be safe to re-run.

### 2.3 Identity module

**`User` aggregate** — `Identity.Domain/User.cs`

```csharp
public sealed class User : AggregateRoot<UserId>
{
    private User() { }                                  // EF only. No validation here.

    public UserId Id { get; private set; }
    public string Email { get; private set; } = null!;   // stored lowercase, normalised in Create
    public string PasswordHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    public static OneOf<User, ValidationFailed> Create(string email, string passwordHash) { … }

    public void ChangePassword(string newHash) { … }
}
```

⚠️ **Do not** add a `private User(UserId id, string email, string passwordHash)`. Those parameter names match mapped properties, so EF's constructor binder will use it for materialisation and run any guards inside it on every `SELECT`. Construct via object initialiser inside `Create`.

**Password hashing** — Argon2id via `Konscious.Security.Cryptography.Argon2`. There is no in-box Argon2 in .NET 10 and there won't be. OWASP parameters: `m=19456` (19 MiB), `t=2`, `p=1`, 16-byte salt, 32-byte output. Encode as a PHC string (`$argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>`) so the parameters travel with the hash and you can rehash on upgrade.

**Tokens** — `JsonWebTokenHandler` (not the older `JwtSecurityTokenHandler`). Refresh tokens are opaque random 32-byte values, **stored hashed** (SHA-256 is fine — they're already high-entropy), rotated on use, with the replaced token marked superseded.

**Commands / queries**

| Type | Result cases |
|---|---|
| `RegisterUserCommand(string Email, string Password)` | `Success(TokenPair)` · `EmailAlreadyUsed` · `ValidationFailed` |
| `LoginUserCommand(string Email, string Password)` | `Success(TokenPair)` · `InvalidCredentials` |
| `RefreshToken(string RefreshToken)` | `Success(TokenPair)` · `InvalidOrExpired` |
| `RevokeToken(string RefreshToken)` | `Success` · `NotFound` |

`InvalidCredentials` is deliberately one case, not "no such user" plus "wrong password" — enumeration disclosure. Also run the hash verification even when the user doesn't exist, against a dummy hash, so the timing doesn't leak.

**Endpoints** — `Identity.Infrastructure/IdentityEndpoints.cs`

```
POST /api/auth/register   201 + TokenPair | 409 Problem | 400 Problem
POST /api/auth/login      200 + TokenPair | 401 Problem
POST /api/auth/refresh    200 + TokenPair | 401 Problem
POST /api/auth/logout     204                                     [Authorize]
GET  /api/auth/me         200 + { id, email }                     [Authorize]
```

Mapping shape:

```csharp
group.MapPost("/login", async (
    LoginRequest req,
    ICommandHandler<LoginUserCommand, LoginUserResult> handler,
    CancellationToken ct) =>
{
    var result = await handler.Handle(new LoginUserCommand(req.Email, req.Password), ct);
    return result.Match<IResult>(
        success => TypedResults.Ok(success.Tokens),
        invalid => TypedResults.Problem(statusCode: 401, title: "Invalid credentials"));
});
```

### 2.4 Persistence

`IdentityDbContext`, schema `identity`, tables `users`, `refresh_tokens`.

```csharp
protected override void OnModelCreating(ModelBuilder b)
{
    b.HasDefaultSchema("identity");
    b.ApplyConfigurationsFromAssembly(
        typeof(IdentityDbContext).Assembly,
        predicate: t => t.Namespace!.StartsWith("StockPortfolio.Modules.Identity", StringComparison.Ordinal));
}
```

```csharp
options.UseNpgsql(cs, npg => {
    npg.MigrationsHistoryTable("__EFMigrationsHistory", "identity");   // ⚠️ mandatory — see §6
    npg.MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName);
});
```

Strongly-typed IDs registered once:

```csharp
protected override void ConfigureConventions(ModelConfigurationBuilder c)
{
    c.Properties<UserId>().HaveConversion<UserIdConverter>();
    c.DefaultTypeMapping<UserId>().HasConversion<UserIdConverter>();   // ← the line people miss
}
```

`UserId.New() => new(Guid.CreateVersion7())`, mapped `ValueGeneratedNever()`. UUIDv7 for index locality; a wrapper ID would otherwise lose Npgsql's sequential-GUID generator, because its selector switches on `property.ClrType`, which is `UserId`, not `Guid`.

Database bootstrap (`db/init/01-roles.sql`, run by the Postgres container's entrypoint and by the Azure migration job):

- Roles `identity_svc`, `portfolio_svc`, `marketdata_svc`, `alerts_svc` — DML only
- Role `migrator` — owns the schemas, has `CREATE`
- Schemas `identity`, `portfolio`, `marketdata`, `alerts`
- `REVOKE ALL ON SCHEMA <other> FROM <role>` so a cross-schema read fails with a permission error

---

## 3. Frontend

Vite + React + TS. Tailwind v4 via `@tailwindcss/vite` and `@import "tailwindcss"` in `index.css` — **no `tailwind.config.js`, no PostCSS**. Every v3 tutorial is wrong on this.

### Routes (file-based, `@tanstack/router-plugin`, `routeTree.gen.ts` committed)

```
src/routes/
  __root.tsx                 QueryClientProvider, devtools, <Outlet/>
  index.tsx                  → redirect to /dashboard or /login
  login.tsx
  register.tsx
  _authenticated.tsx         guard + app shell (nav, user email, logout)
  _authenticated/dashboard.tsx    empty shell this phase
```

Guard:

```tsx
export const Route = createFileRoute('/_authenticated')({
  beforeLoad: ({ context, location }) => {
    if (!context.auth.isAuthenticated) {
      throw redirect({ to: '/login', search: { redirect: location.href } })
    }
  },
  component: AppShell,
})
```

Router context is typed via `createRouter({ context: { auth: undefined!, queryClient } })` and filled at render with `<RouterProvider router={router} context={{ auth }} />`.

### Session

- **Access token in memory** (a module-scoped variable + React context). Not `localStorage` — worth a README line.
- **Refresh token in an httpOnly cookie** for the compose deployment (same origin through nginx). For GitHub Pages the origins differ, so the refresh token is returned in the body and held in memory, and the page bootstraps by calling `/api/auth/refresh` on load with a token persisted in `sessionStorage`. State the trade-off in the README; it is the honest consequence of a static-host deployment.
- `apiClient` wrapper attaches `Authorization: Bearer`, and on 401 refreshes then retries once.

⚠️ **Dedupe the refresh.** Hold a single in-flight promise so ten concurrent 401s trigger one refresh:

```ts
let refreshInFlight: Promise<string> | null = null
async function refresh(): Promise<string> {
  refreshInFlight ??= doRefresh().finally(() => { refreshInFlight = null })
  return refreshInFlight
}
```

### Components (hand-built, mockup layout minus ornament)

`Button`, `TextField`, `Card`, `Alert`, `Spinner`, `AppShell` with nav. No hero section, no ticker strip.

---

## 4. Infrastructure delta

Everything. `infra/main.bicep` + `infra/modules/*.bicep`, parameters in `infra/main.bicepparam`, names suffixed with `uniqueString(resourceGroup().id)`.

| Resource | Key settings |
|---|---|
| `Microsoft.App/managedEnvironments` | Consumption workload profile · `appLogsConfiguration.destination: 'none'` (Log Analytics is optional and its ingestion is the sneakiest line on the bill) |
| `Microsoft.App/containerApps` — API | `external: true` · `targetPort: 8080` (ASP.NET Core 8+ listens on 8080, **not** 80) · `minReplicas: 1` · `maxReplicas: 2` · `corsPolicy.allowedOrigins: [pagesOrigin]` · `allowCredentials: true` |
| `Microsoft.DBforPostgreSQL/flexibleServers` | `Standard_B1ms` Burstable · 32 GB · public access + `AllowAllAzureServicesAndResourcesWithinAzureIps` firewall rule (ACA Consumption reaches it with no VNet) |
| `Microsoft.Cache/redisEnterprise` — AMR | Balanced **B0** · high availability **disabled** |
| `Microsoft.ContainerRegistry/registries` | Basic · managed-identity pull |
| `Microsoft.ManagedIdentity/userAssignedIdentities` | One UAMI for everything. System-assigned cannot be used for Key Vault refs in a single-pass deploy |
| `Microsoft.App/jobs` — migrations | `triggerType: 'Manual'` · `replicaCompletionCount: 1` · `parallelism: 1` · `replicaTimeout: 600` · runs an EF migrations bundle as `migrator` |

Secrets as plain ACA secrets — Key Vault is not worth the round trip at this scale. Connection strings carry `Maximum Pool Size=2`.

⚠️ **The `AcrPull` role assignment races the container app** in a single deployment. Put the role assignment in its own module and `dependsOn` it from the container app.

### Workflows

`.github/workflows/ci.yml` — on PR: `dotnet build`, `dotnet test`, `npm test`, `bicep build`, `az deployment group what-if`.

`.github/workflows/deploy.yml` — on push to `main`:
1. `azure/login` with **OIDC federated credentials** (no stored secret)
2. Build + push API image to ACR
3. `az deployment group create`
4. `az containerapp job start -n job-migrate-<suffix>` and wait for success
5. `az containerapp update` with the new image
6. Build the SPA with `VITE_API_BASE_URL=<api fqdn>` and `--base=/<repo>/`, copy `dist/index.html` → `dist/404.html`, publish to Pages

### Compose

```yaml
services:
  postgres:   healthcheck: pg_isready -U $POSTGRES_USER -d $POSTGRES_DB
  redis:      command: redis-server --appendonly yes --appendfsync everysec
  migrations: depends_on: { postgres: { condition: service_healthy } }
  api:        depends_on: { migrations: { condition: service_completed_successfully } }
  web:        nginx, proxies /api → api
```

⚠️ The Postgres healthcheck **must** pass `-U` and `-d`. A bare `pg_isready` returns healthy while the init scripts are still running, and the migration container then fails against a database with no roles.

⚠️ nginx `location /api/alerts/stream` needs `proxy_buffering off; gzip off; proxy_read_timeout 3600s; proxy_http_version 1.1;` — set it now even though Phase 4 uses it. Its default `proxy_read_timeout 60s` is *stricter* than ACA's 240s.

---

## 5. Tests

### Unit — `Identity.UnitTests`

| Test | Asserts |
|---|---|
| `Create_WithMalformedEmail_ReturnsValidationFailed` | Email shape rejected, no exception thrown |
| `Create_NormalisesEmailToLowercase` | `Foo@Bar.com` stored as `foo@bar.com` |
| `Argon2_VerifyRoundTrip_Succeeds` | Hash then verify returns true |
| `Argon2_WrongPassword_Fails` | Returns false, not an exception |
| `Argon2_ProducesDistinctHashesForSamePassword` | Salt is actually random |
| `PhcString_RoundTrips_Parameters` | m/t/p survive encode→decode |
| `RefreshToken_Rotate_SupersedesPrevious` | Old token marked superseded, new one active |
| `RefreshToken_UseSuperseded_Fails` | Replay rejected |

### Unit — `Shared.Kernel.UnitTests`

`Money_Add_SameCurrency_Sums` · `Money_Add_DifferentCurrency_Throws` · `UserId_Converter_RoundTrips` · `UserId_New_ProducesSortableGuids` (v7 monotonicity)

### Integration — `Api.IntegrationTests`

Testcontainers Postgres + Redis, one collection fixture, `WebApplicationFactory<Program>`.

| Test | Asserts |
|---|---|
| `Migrations_ApplyCleanly_OnEmptyDatabase` | All four contexts migrate; four schemas exist |
| `Register_ThenLogin_ReturnsTokens` | 201 then 200 with a non-empty token pair |
| `Register_DuplicateEmail_Returns409` | Second registration conflicts |
| `Register_WeakPassword_Returns400WithProblemDetails` | `application/problem+json`, field-level errors |
| `Me_WithoutToken_Returns401` | |
| `Me_WithValidToken_ReturnsEmail` | |
| `Refresh_RotatesToken_OldOneRejected` | Second use of the old refresh token → 401 |
| `Health_ReturnsHealthy_WithPostgresAndRedis` | Both checks reported |
| `PortfolioRole_CannotReadIdentitySchema` | Connecting as `portfolio_svc` and selecting from `identity.users` throws a permission error — **this is the test that proves the role isolation is real rather than aspirational** |

### Architecture — `Architecture.Tests`

| Test | Asserts |
|---|---|
| `Modules_DoNotReferenceOtherModulesInternals` | Only `*.Contracts` crosses a module boundary |
| `ContractsProjects_HaveNoEfCoreReference` | |
| `DomainTypes_HaveNoPublicSetters` | Reflection over `Modules.*.Domain` |

### Frontend — `Web`

Vitest + RTL + MSW: `redirects unauthenticated visit to /login` · `login form shows server error without crashing` · `concurrent 401s trigger exactly one refresh call` (MSW request counter)

---

## 6. Gotchas

**`HasDefaultSchema` does not move `__EFMigrationsHistory`.** efcore#24127, closed *not planned*. Without `MigrationsHistoryTable(name, schema)` on every context, all four share `public.__EFMigrationsHistory`, each sees the others' migration IDs, and `database update` reports migrations as applied-but-missing. Looks like corruption. Set it on day one.

**Do not put `SearchPath=` in the connection string.** Two open Npgsql bugs (efcore.pg#3359, #2917) make it fail migrations with `42P07: relation "__EFMigrationsHistory" already exists`. Explicit `MigrationsHistoryTable` sidesteps the whole class.

**`ApplyConfigurationsFromAssembly` silently skips configurations with constructor parameters**, logging only `SkippedEntityTypeConfigurationWarning`. Add `.ConfigureWarnings(w => w.Throw(CoreEventId.SkippedEntityTypeConfigurationWarning))` in Development.

**JWT claim mapping.** Set `MapInboundClaims = false` **and** explicit `NameClaimType`/`RoleClaimType` in `TokenValidationParameters`, or `User.FindFirst("sub")` silently returns null because the handler renamed it to the long Microsoft claim URI.

**ASP.NET Core listens on 8080** since .NET 8, not 80. `targetPort: 8080` in Bicep and `EXPOSE 8080` in the Dockerfile.

**The base image creates an `app` user but only `*-chiseled` images set `USER`.** Without an explicit `USER app` line you run as root.

**GitHub Pages + SPA routing.** Copy `index.html` to `404.html` at build time, set Vite `base: '/<repo>/'`, and give the router a matching `basepath`. Without all three you get a 404 on any deep link and a blank page on refresh.

**`VITE_*` variables are build-time only and are not secrets.** The API base URL is baked into the bundle by the Actions job. Never put a key there.

---

## 7. Your call

### Token lifetimes — `Identity.Application/TokenPolicy.cs`

The file will exist with the signature and a comment block. Fill in:

```csharp
internal static class TokenPolicy
{
    // TODO(you): Access-token TTL, refresh-token TTL, and whether refresh rotates on every use.
    //
    // Trade-offs:
    //   Short access TTL  → smaller stolen-token window, more refresh round-trips.
    //   Rotate-on-use     → detects replay, but breaks concurrent tabs unless you allow a
    //                       grace period where the superseded token still works briefly.
    //   Long refresh TTL  → fewer logins, larger blast radius if the token store leaks.
    //
    // Note this interacts with the GitHub Pages deployment: the refresh token lives in
    // sessionStorage there, not an httpOnly cookie, which argues for a shorter TTL.
    public static TimeSpan AccessTokenLifetime => …;
    public static TimeSpan RefreshTokenLifetime => …;
    public static bool RotateOnUse => …;
    public static TimeSpan RotationGracePeriod => …;
}
```

~8 lines. The values propagate into the integration tests, so pick before writing `Refresh_RotatesToken_OldOneRejected`.

---

## 8. Done when

- [ ] `docker compose up` from a clean clone → `http://localhost:5173` serves the app, `/health` reports Healthy with both Postgres and Redis
- [ ] Register a new account → land on the dashboard shell
- [ ] Hard-refresh → still signed in
- [ ] Log out → navigating to `/dashboard` bounces to `/login` with `?redirect=` set
- [ ] Log in again → returned to `/dashboard`, not the home page
- [ ] `dotnet test` green, including `PortfolioRole_CannotReadIdentitySchema`
- [ ] `npm test` green
- [ ] `bicep build` and `az deployment group what-if` clean
- [ ] Deployed: `https://<user>.github.io/<repo>/` talks to `https://ca-api-<suffix>.<region>.azurecontainerapps.io` — register and log in there
- [ ] Deep-link straight to `https://<user>.github.io/<repo>/login` → loads (proves the `404.html` fallback)
- [x] OneOf toolchain verified against Roslyn 5 — `OneOfDiagnosticSuppressor` does not exist and is unnecessary; see `phase-1-implementation.md` §3
- [ ] README: run instructions, the token-storage decision, the Postgres role isolation
- [ ] Screens usable at 375px
