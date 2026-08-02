# Phase 1 — Implementation plan

Companion to [phase-1-sign-in.md](phase-1-sign-in.md). That file says *what* Phase 1 must do and which traps to avoid. This one says *which files exist, in which project, referencing what, built in which order*.

Nothing exists yet. Every path below is created in this phase.

**Scope reminder** — brief P0 req 1 (auth + session persistence), the auth half of req 3 (TanStack Router), req 6 (parameterised DB access), req 7 (`docker compose up`). Plus the whole build, test, container and deploy skeleton that phases 2–6 add deltas to.

> **Revision note.** This document was reviewed before any code was written; the review found six blockers and eleven majors, all folded in below. Three decisions since then changed the shape of the design: **§4.2** accessibility follows the onion rather than blanket `internal`; **§4.5** shape validation is an `IEndpointFilter`, not a DI decorator; **§4.6** endpoints live in a new `.Presentation` project, not in `.Infrastructure`. §13 lists what they cost.

---

## 1. Naming and root conventions

| Thing | Value |
|---|---|
| Solution | `StockPortfolio.slnx` (.NET 10 supports the XML solution format; `dotnet sln` reads it natively) |
| Root namespace / assembly prefix | `StockPortfolio.` |
| Module namespace | `StockPortfolio.Modules.<Module>.<Layer>` |
| Host | `StockPortfolio.Api` |
| Kernel | `StockPortfolio.Shared.Kernel` |
| TFM | `net10.0`, `LangVersion` 14 |
| SDK | `global.json` pinned `10.0.302`, `rollForward: latestFeature` — **and the Docker build must pin `mcr.microsoft.com/dotnet/sdk:10.0.302`**, because `rollForward` only rolls *forward*: a base image carrying an older 10.0.2xx SDK fails the container build with "compatible SDK version not found" while local builds stay green |

The namespace prefix is load-bearing, not cosmetic: `IdentityDbContext.OnModelCreating` filters `ApplyConfigurationsFromAssembly` with `t.Namespace!.StartsWith("StockPortfolio.Modules.Identity")`. Get the prefix wrong and configurations are silently skipped.

---

## 2. Repository layout — everything Phase 1 creates

```
StockPortfolio.slnx
global.json
Directory.Build.props            repo-wide: TFM, nullable, warnings, analyzers
Directory.Build.targets          late-bound only (see §3)
Directory.Packages.props         Central Package Management — all versions live here
.editorconfig
.dockerignore
docker-compose.yml
docker-compose.override.yml
.env.example
README.md

db/
  init/00-roles.sh               wrapper: passes passwords to psql as -v variables
  init/01-roles.sql              schemas, roles, grants, revokes — all four modules

src/
  Shared.Kernel/                 no ASP.NET Core reference — see §4.7
    AggregateRoot.cs
    IDomainEvent.cs
    Money.cs
    Cqrs/ICommandHandler.cs
    Cqrs/IQueryHandler.cs
    Cqrs/ValidationFailed.cs

  Shared.Presentation/           FrameworkReference Microsoft.AspNetCore.App
    IEndpointModule.cs
    ValidationFilter.cs          the generic IEndpointFilter — §4.5
    ProblemDetailsExtensions.cs

  Modules/
    Identity/     Contracts + Domain + Application + Infrastructure + Presentation — §5.0
    Portfolio/    same five projects — empty shells
    MarketData/   empty shells
    Alerts/       empty shells

  Migrator/
    StockPortfolio.Migrator.csproj                  console; applies every context as `migrator`
    Program.cs

  Api/
    StockPortfolio.Api.csproj
    Program.cs
    Extensions/{Authentication,HealthCheck,Decorator}Extensions.cs
    Middleware/ApiExceptionHandler.cs
    Decorators/LoggingDecorator.cs           validation is a filter now — §4.5
    appsettings.json
    appsettings.Development.json
    StockPortfolio.Api.http
    Dockerfile

  Web/                                          see §8

tests/
  StockPortfolio.Shared.Kernel.UnitTests/
  StockPortfolio.Modules.Identity.UnitTests/
  StockPortfolio.Api.IntegrationTests/
  StockPortfolio.Architecture.Tests/

infra/
  main.bicep  main.bicepparam
  modules/{loganalytics,acr,identity,postgres,redis,
           containerapp-env,containerapp-api,job-migrate,roleassignment}.bicep

.github/workflows/{ci,deploy}.yml
```

**Empty shells for Portfolio / MarketData / Alerts are deliberate.** They cost ten minutes now and they make the architecture tests meaningful from day one — a boundary rule that only has one module to check is not a rule. They also make `Directory.Packages.props` and the solution graph final, so phases 2–4 add files, never plumbing.

---

## 3. Build infrastructure

### `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <!-- NuGet audit advisories must not break a build mid-phase -->
    <WarningsNotAsErrors>$(WarningsNotAsErrors);NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>

    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>10.0-recommended</AnalysisLevel>   <!-- pinned, not `latest-` -->
    <ArtifactsPath>$(MSBuildThisFileDirectory)artifacts</ArtifactsPath>
  </PropertyGroup>
</Project>
```

Four corrections over the obvious version of this file:

**`TreatWarningsAsErrors=true` already makes CS8509 an error**, so listing it in `WarningsAsErrors` is a no-op — and, more importantly, *removing* it from that list would not un-error it. The only working escape is `<WarningsNotAsErrors>CS8509</WarningsNotAsErrors>`. It is not currently needed; see the spike results below.

**`AnalysisLevel` is pinned to `10.0-recommended`, not `latest-recommended`.** `latest-` is a floating value: a new SDK adds analyzer warnings and, under `TreatWarningsAsErrors`, breaks the build with no repo change. §13 calls floating package versions a reproducibility hole; this is the same hole.

**`EnforceCodeStyleInBuild` is deliberately absent.** With warnings-as-errors it promotes every `IDExxxx` suggestion — including inside generated EF migration files — to a build error. Run style as `dotnet format --verify-no-changes` in CI instead, where it is a separate, readable failure. Set style rules to `suggestion` severity in `.editorconfig`.

**`ArtifactsPath`** changes output layout to `artifacts/bin/<project>/<config>`, so the Dockerfile must publish with an explicit `-o /app` rather than assuming `bin/Release/net10.0/publish`.

`Directory.Build.targets` exists but is empty in Phase 1. It is where anything conditional on `$(TargetFramework)` must go — that property is empty during `.props` evaluation for single-targeting projects, so a condition on it there silently never matches.

### `Directory.Packages.props`

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup><!-- domain / application -->
    <PackageVersion Include="OneOf" Version="3.0.271" />
    <PackageVersion Include="FluentValidation" Version="12.0.0" />
    <PackageVersion Include="FluentValidation.DependencyInjectionExtensions" Version="12.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup><!-- infrastructure -->
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.7" />
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
    <PackageVersion Include="Konscious.Security.Cryptography.Argon2" Version="1.3.1" />
    <PackageVersion Include="Microsoft.IdentityModel.JsonWebTokens" Version="8.19.1" />
    <PackageVersion Include="StackExchange.Redis" Version="3.1.0" />
  </ItemGroup>

  <ItemGroup><!-- host -->
    <PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore"
                    Version="10.0.0" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.7" />
  </ItemGroup>

  <ItemGroup><!-- tests -->
    <PackageVersion Include="xunit.v3" Version="3.1.0" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
    <PackageVersion Include="Testcontainers.PostgreSql" Version="4.6.0" />
    <PackageVersion Include="Testcontainers.Redis" Version="4.6.0" />
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.0.0" />
    <PackageVersion Include="Shouldly" Version="4.3.0" />
  </ItemGroup>

  <ItemGroup><!-- applies to every project -->
    <GlobalPackageReference Include="OneOf.SourceGenerator" Version="3.0.271" />
  </ItemGroup>
</Project>
```

Three things the first draft of this file got wrong:

- **`Microsoft.AspNetCore.OpenApi` is a NuGet package, not part of the shared framework.** `builder.Services.AddOpenApi()` does not compile without it.
- **`AddValidatorsFromAssemblyContaining<T>` lives in `FluentValidation.DependencyInjectionExtensions`**, a separate package from `FluentValidation` core.
- **`AspNetCore.HealthChecks.NpgSql` / `.Redis` are dropped.** Those community packages are built against Npgsql 8/9 and StackExchange.Redis 2.x while we pin Npgsql EF 10.0.3 and SE.Redis 3.1.0 — with transitive pinning on and no `PackageVersion` for bare `Npgsql`, that is a restore-time downgrade conflict on day one. `AddDbContextCheck<IdentityDbContext>()` covers Postgres and a Redis PING check is ten lines over the multiplexer we own anyway.

`Microsoft.EntityFrameworkCore.Design` goes on the **`Api`** project (the `--startup-project`) with `PrivateAssets="all"`, not on the Infrastructure projects.

`GlobalPackageReference` for the two OneOf analyzers is the right tool — they must be present in every project that declares or matches a union, and CPM applies `PrivateAssets="All"` to them automatically.

### The step-2 spike — done, and it changed this section

Run 2026-08-02 against SDK 10.0.302. Three results, all folded into the files above.

**`OneOfDiagnosticSuppressor` does not exist on nuget.org.** `00-overview.md` lists it in the "versions verified" stack; that entry was wrong. It is also **not needed**: `.Match(...)` takes one delegate per case, so exhaustiveness is enforced by the method's arity — add a fourth case to a union and every call site fails to compile. `CS8509` only fires on a `switch` over `.Value`, which the convention forbids, and `TreatWarningsAsErrors` already makes that an error. The guarantee the suppressor was supposed to provide is structural.

**`[GenerateOneOf]` crashes on types in the global namespace.** The generator builds its hint name from the namespace and emits `<global namespace>_RegisterResult.g.cs`; `<` is an illegal filename character, so it throws:

```
error CS8785: Generator 'OneOfGenerator' failed to generate source.
  ArgumentException: The hintName '<global namespace>_RegisterResult.g.cs'
  contains an invalid character '<' at position 0.
```

and every downstream implicit conversion then fails with confusing `CS7036`/`CS0029` errors that point nowhere near the cause. Inside a namespace it works cleanly on Roslyn 5. All our code is namespaced, so this is a trap for scratch files and spikes rather than production code — but it cost twenty minutes to diagnose once.

**Three build-configuration bugs surfaced immediately**, each fixed in the files above:

| Symptom | Cause | Fix |
|---|---|---|
| `NU1506` duplicate `PackageVersion` on every project | `OneOf.SourceGenerator` declared as *both* `PackageVersion` and `GlobalPackageReference` | `GlobalPackageReference` carries its own `Version`; drop the `PackageVersion` |
| `CA1707` error in all four test projects | underscores in `Method_Scenario_Expectation` names, promoted by `TreatWarningsAsErrors` | `tests/Directory.Build.props` with `NoWarn` — and it must explicitly `<Import>` the root props, since MSBuild only auto-imports the *first* one found walking up |
| `MSB4092: unexpected token "Directory"` | `Exists('$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', …)))` nests single quotes | hoist the path into a property, then condition on the property |

**And one supply-chain finding.** `Microsoft.AspNetCore.OpenApi` 10.0.10 pulls `Microsoft.OpenApi` **2.0.0**, which carries a high-severity advisory (GHSA-v5pm-xwqc-g5wc). Transitive pinning is on, so naming it in `Directory.Packages.props` fixes it — but **3.x is not the answer**: it makes `IOpenApiMediaType.Example` read-only while the ASP.NET Core OpenAPI source generator still assigns to it, so the build fails with `CS0200` inside generated code. Pin **2.11.0**.

Putting `NU1901;NU1902;NU1903;NU1904` in `WarningsNotAsErrors` earned its keep on the first build: without it the advisory would have been a hard build failure rather than a warning to act on deliberately.

---

## 4. Project graph and the two structural decisions

### 4.1 References

| Project | References |
|---|---|
| `Shared.Kernel` | — (`OneOf` only). **No ASP.NET Core** |
| `Shared.Presentation` | `Shared.Kernel`, FluentValidation, `FrameworkReference Microsoft.AspNetCore.App` |
| `<M>.Contracts` | — (nothing; records of primitives) |
| `<M>.Domain` | `Shared.Kernel` |
| `<M>.Application` | `<M>.Domain`, `<M>.Contracts`, other modules' `.Contracts` only |
| `<M>.Infrastructure` | `<M>.Application`, EF Core, Npgsql. **No ASP.NET Core** |
| `<M>.Presentation` | `<M>.Application`, `Shared.Presentation`. **No EF Core, no `.Infrastructure`** |
| `Migrator` | every `<M>.Infrastructure` |
| `Api` | every `<M>.Infrastructure` *and* `<M>.Presentation`, `EFCore.Design` (`PrivateAssets="all"`) |
| `Architecture.Tests` | every project (it reflects over them) |

The two "no" columns are the point of the split and both are asserted by `Architecture.Tests`: **`.Infrastructure` never sees HTTP, `.Presentation` never sees the database.** A route cannot reach a `DbContext` without going through a handler, because the reference does not exist.

### 4.2 DECISION — accessibility is onion-per-module, not internal-everywhere

The design doc's rule is *"everything is `internal` outside `.Contracts`."* That rule does not survive contact with the project layout, because **`internal` is per-assembly and a module is three assemblies**. `Identity.Infrastructure` cannot see an `internal User` in `Identity.Domain`; `Identity.Application` cannot expose an `internal RegisterUser` to the endpoint that injects its handler. Making it work would need an `InternalsVisibleTo` matrix in every module — Domain → Application/Infrastructure/UnitTests, Application → Infrastructure/Api/UnitTests.

**Settled: layer visibility follows the onion, enforced by ProjectReferences.**

| Layer | Accessibility | Visible to |
|---|---|---|
| `.Contracts` | `public` | every module |
| `.Domain` | `public` | its own module only (by ProjectReference) |
| `.Application` | `public` | `.Infrastructure`, `.Presentation` |
| `.Infrastructure` | **`internal`**, except `<M>Module` | the host, through one seam |
| `.Presentation` | `public` | the host only — it is a leaf, nothing references it |

Infrastructure stays internal because nothing outside the module has any business naming `IdentityDbContext`, `UserRepository` or `Argon2PasswordHasher` — that is the layer where leaks actually happen.

Presentation is public rather than internal, deliberately. It is a leaf project that only `Api` references, so there is no encapsulation to protect; and minimal API model binding, `System.Text.Json` and the OpenAPI document generator all behave better with public request/response records. Trading a theoretical boundary for a class of serializer bugs is a bad trade in a 1.25-day phase.

**What this costs, stated plainly:** the compiler no longer prevents `Portfolio.Application` from using `Identity.Domain.User` if someone adds the ProjectReference. `Architecture.Tests.Modules_DoNotReferenceOtherModulesInternals` becomes the only enforcement, so it is now load-bearing rather than decorative. That is a real trade and belongs in the README.

**Two knock-on corrections:**

- `CLAUDE.md` states the internal-everywhere rule as a non-negotiable. It now contradicts the code. Update that line to the table above, or the next reader follows the wrong rule.
- §5.4's original argument for rejecting .NET 10's built-in `AddValidation()` was *"it would need `InternalsVisibleTo`, which punctures the rule."* With Application public, that reason evaporates. The surviving reason is different and better — see §4.5.

### 4.3 The public seam

A module now has **two** public entry points, one per direction, because registration needs Infrastructure types and routing needs Presentation types.

`Infrastructure/IdentityModule.cs` — everything the DI container needs:

```csharp
namespace StockPortfolio.Modules.Identity.Infrastructure;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<IdentityDbContext>(o => o.UseNpgsql(
            config.GetConnectionString("Identity"),
            npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "identity")));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddIdentityHandlers();          // internal, same assembly
        return services;
    }
}
```

`Presentation/IdentityEndpoints.cs` — everything the router needs:

```csharp
namespace StockPortfolio.Modules.Identity.Presentation;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app) { … }
}
```

`Api/Program.cs` therefore calls two lines per module, and can name nothing else in either project.

`AddDbContext<IdentityDbContext>()` with an `internal` context inside a `public` method **compiles**. Inconsistent-accessibility rules (CS0051/CS0053) apply to signatures — parameter and return types — not to generic arguments used inside a method body.

`MigrationsAssembly(...)` is **dropped**: migrations land in `Persistence/Migrations` of the same assembly as the context, which is already the default. Leaving it in implies a split that does not exist.

`IEndpointModule` moves to `Shared.Presentation` (§4.7) and stays unused in Phase 1 — `app.MapIdentityEndpoints()` is one line, trim-safe and explicitly ordered.

### 4.4 Handler and validator registration

Handlers are registered by `.Infrastructure` (it owns the concrete repositories they need):

```csharp
internal static IServiceCollection AddIdentityHandlers(this IServiceCollection s)
{
    s.AddScoped<ICommandHandler<RegisterUser, RegisterResult>, RegisterUserHandler>();
    s.AddScoped<ICommandHandler<LoginUser,    LoginResult>,    LoginUserHandler>();
    // …
    return s;
}
```

Validators are registered by `.Presentation`, because that is where they and the records they check now live (§4.5):

```csharp
public static IServiceCollection AddIdentityPresentation(this IServiceCollection s)
    => s.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();
```

With Presentation types public, `includeInternalTypes: true` is not required on the FluentValidation scanner. If a validator is ever made internal, that flag comes back — and the failure mode is silent (zero validators registered, zero errors), which is the second reason §4.5 injects `IValidator<T>` rather than `IEnumerable<IValidator<T>>`.

**`LoggingDecorator` survives; `ValidationDecorator` does not.** Logging is genuinely cross-cutting over handlers and has no `TResult` problem — it passes the result straight through. Register it with `Decorate<,>` in `Api/Extensions/DecoratorExtensions.cs`, after the modules so the concrete registrations exist.

> ⚠️ Do **not** add a transaction decorator in Phase 1. It becomes wrong the moment `EnableRetryOnFailure` is switched on, because the retry strategy must own the transaction (`CreateExecutionStrategy().ExecuteAsync(...)`). Register it when a handler actually writes two aggregates.

### 4.5 DECISION — validation is an `IEndpointFilter`, not a DI decorator

The generic decorator from the design doc cannot work as written:

```csharp
internal sealed class ValidationDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IEnumerable<IValidator<TCommand>> validators) : ICommandHandler<TCommand, TResult>
```

On failure it must return a `TResult`. `TResult` is unconstrained, and `[GenerateOneOf]`'s conversion from `ValidationFailed` is a **user-defined operator on a concrete type**, unreachable through a type parameter. `LoginResult` has no `ValidationFailed` case at all, so no amount of reflection could produce one either. The workaround was going to be throwing an exception and catching it in middleware.

**Settled instead: a generic `IEndpointFilter` in `Shared.Presentation`.** A filter sits in the HTTP pipeline rather than the DI graph, so it can *return* a response and short-circuit — the unconstrained-`TResult` problem simply does not arise, and neither does the throw/catch round trip.

```csharp
// Shared.Presentation/ValidationFilter.cs
public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (ctx.Arguments.OfType<TRequest>().FirstOrDefault() is not { } request)
            return await next(ctx);

        var result = await validator.ValidateAsync(request, ctx.HttpContext.RequestAborted);
        return result.IsValid
            ? await next(ctx)
            : TypedResults.ValidationProblem(result.ToDictionary());
    }
}
```

Applied per endpoint, so the HTTP contract of a route is readable in one place:

```csharp
group.MapPost("/login", LoginAsync)
     .AddEndpointFilter<ValidationFilter<LoginRequest>>()
     .WithName("Login");
```

Register the validators once in `MapIdentityEndpoints`' companion DI call:
`services.AddValidatorsFromAssemblyContaining<LoginRequestValidator>();`

The three-layer split is intact; only the top layer changed mechanism:

| Failure kind | Where | Mechanism |
|---|---|---|
| Shape — "is this even an email?" | FluentValidation on the **request**, in `.Presentation` | filter returns **400** |
| Context — "does this user exist? allowed?" | handler, `.Application` | OneOf result case |
| Invariant — "a User can never have a blank email" | entity, `.Domain` | **throws** |

⚠️ **The filter runs on the request DTO, not the command.** That is correct now that `.Presentation` exists — a transport concern validated in the transport layer — but it means a hypothetical second, non-HTTP caller of a handler would bypass the rules. There is exactly one caller per handler today (the argument for CQRS without a dispatcher), so this costs nothing; if a background job ever calls a handler directly, its inputs need their own guard.

⚠️ **`IValidator<TRequest>` is injected, not `IEnumerable<IValidator<TRequest>>`.** With the single-instance form, DI throws at request time if a validator is missing — loud, immediate, and it fails the integration test. Injecting the collection makes a missing validator silently validate nothing.

**The built-in .NET 10 `AddValidation()` is still not used**, and the reason has changed now that we validate the DTO. It is driven by `System.ComponentModel.DataAnnotations` attributes, which are fine for `[Required]` and `[EmailAddress]` but awkward as soon as a rule is conditional, spans two fields, or needs a lookup. A FluentValidation `AbstractValidator` handles all three in ordinary C#. Say that in the README — it is a real evaluation, not an omission.

### 4.6 DECISION — endpoints live in `.Presentation`

An earlier draft put `IdentityEndpoints.cs` in `.Infrastructure` to avoid a fifth project. **That was wrong and is reversed.** Infrastructure means persistence and *outbound* integrations — the database, the quote provider, the hasher. Inbound HTTP is presentation. Parking routes next to `IdentityDbContext` forced one project to carry `FrameworkReference Microsoft.AspNetCore.App` *and* EF Core, which is precisely the mixing the layering exists to prevent.

Moving them to `Api` would also have fixed the layering, but it makes the host the file every future feature edits and breaks the "a module is N folders away from being its own service" property. `.Presentation` fixes the layering *and* keeps the module whole.

Cost: four extra projects, ten minutes in §12 step 1. In exchange, two reference rules become compiler-enforced (§4.1) rather than conventions.

### 4.7 `Shared.Presentation` — and a bug it fixes

`Shared.Kernel` was carrying `Endpoints/IEndpointModule.cs`, whose signature takes `IEndpointRouteBuilder` — an ASP.NET Core type. That would have forced `FrameworkReference Microsoft.AspNetCore.App` onto `Shared.Kernel`, and therefore transitively onto every `.Domain` project. The kernel holds `Money` and `AggregateRoot`; it must stay framework-free.

So HTTP-shaped shared code moves to a new `Shared.Presentation`:

| File | Purpose |
|---|---|
| `IEndpointModule.cs` | defined, unused in Phase 1 (§4.3) |
| `ValidationFilter.cs` | the generic filter (§4.5) |
| `ProblemDetailsExtensions.cs` | shared `.Match` → `TypedResults` helpers |

Each `<M>.Presentation` references it. `Shared.Kernel` references nothing but `OneOf`, and `Architecture.Tests` asserts it.

---

## 5. Identity module, file by file

Every project below is prefixed `StockPortfolio.Modules.Identity.` on disk; the prefix is dropped here so the shape is readable.

### 5.0 The shape

```
Identity/
│
├── Contracts/                        empty - nothing calls Identity
│
├── Domain/                           the rules. no database, no HTTP
│   ├── UserId.cs                     the id type for a user
│   ├── User.cs                       email + password hash
│   ├── RefreshToken.cs               one login session
│   └── Errors.cs                     the named failure cases
│
├── Application/                      one folder per user action
│   ├── Abstractions/                 interfaces the outer layers fill in
│   │   ├── IPasswordHasher.cs
│   │   ├── ITokenIssuer.cs
│   │   ├── IUserRepository.cs
│   │   ├── IRefreshTokenRepository.cs
│   │   └── IUnitOfWork.cs
│   ├── TokenPair.cs                  what login hands back
│   ├── TokenPolicy.cs                <- YOURS: how long tokens live
│   ├── Register/                     command . result . handler
│   ├── Login/                        same three files
│   ├── Refresh/                      trade refresh token for a new pair
│   ├── Revoke/                       log out
│   └── Me/                           read the signed-in user
│
├── Infrastructure/                   database, hashing, tokens
│   ├── IdentityModule.cs             * wires the module into DI
│   ├── Persistence/                  the database
│   │   ├── IdentityDbContext.cs      owns the 'identity' schema
│   │   ├── Configurations/           tables, columns, indexes
│   │   ├── Converters/               UserId <-> a plain database guid
│   │   ├── UserRepository.cs         insert; duplicate email -> result
│   │   ├── RefreshTokenRepository.cs
│   │   ├── DesignTimeFactory.cs      lets dotnet ef run without config
│   │   └── Migrations/               generated by dotnet ef
│   └── Security/                     passwords and tokens
│       ├── Argon2PasswordHasher.cs   hashes passwords
│       ├── PhcString.cs              keeps hash settings with the hash
│       ├── JwtTokenIssuer.cs         signs the access token
│       └── JwtOptions.cs             signing key, read from config
│
└── Presentation/                     the HTTP surface. no database
    ├── IdentityEndpoints.cs          * the five /api/auth/* routes
    ├── Requests.cs                   what comes in
    ├── Responses.cs                  what goes back
    └── Validators/                   run by the filter, before the route
        ├── RegisterRequestValidator.cs
        ├── LoginRequestValidator.cs
        └── RefreshRequestValidator.cs
```

Read it top to bottom as one slice: a route in `Presentation/` calls a handler in `Application/`, which asks `Domain/` whether the operation is legal and `Infrastructure/` to store the result. `Presentation` has no reference to `Infrastructure` and vice versa — they meet only through the interfaces in `Application/Abstractions/`.

`Register/`, `Login/`, `Refresh/`, `Revoke/` and `Me/` each hold three files: the command, its result union, and the handler. Validators are **not** there — they validate the HTTP request, so they sit in `Presentation/Validators/` next to the records they check (§4.5).

`Domain`, `Application` and `Presentation` are `public`; everything under `Infrastructure` is `internal` apart from `IdentityModule` (§4.2). The two files marked `*` are the module's entire public surface to the host.

### 5.1 `Identity.Contracts` is empty — and that is the finding

Nothing calls Identity at runtime; the JWT carries the user id. So the Contracts project has no types in Phase 1.

**Create it anyway, empty**, with a one-line README inside saying why. It is the argument the module-interactions diagram makes — "Identity is the cheapest module to extract because nothing points at it" — as an artifact rather than a claim.

### 5.2 `Identity.Domain`

`AggregateRoot` declares the Id — the type parameter must earn its place:

```csharp
public abstract class AggregateRoot<TId> where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public TId Id { get; protected set; } = default!;

    [NotMapped]
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent e) => _domainEvents.Add(e);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
```

The design doc's version declares no `Id` at all, leaving `TId` decorative — `AggregateRoot` and `AggregateRoot<TId>` would behave identically. Declaring it on the base is not cosmetic: **`User` must then not re-declare `Id`**, because a re-declaration is CS0108 (hides inherited member) which, under `TreatWarningsAsErrors`, is a build error. EF maps the inherited property normally.

`[NotMapped]` needs no EF reference — `NotMappedAttribute` lives in `System.ComponentModel.Annotations`, part of the shared framework. `Shared.Kernel` stays EF-free.

`UserId.cs`

```csharp
public readonly record struct UserId(Guid Value)
{
    public static UserId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}
```

`Guid.CreateVersion7()` is in-box since .NET 9. UUIDv7 for index locality — and note *why* generating it in the domain matters: Npgsql's sequential-GUID generator selects on `property.ClrType`, which here is `UserId`, not `Guid`, so it would never fire. Generate v7 in the domain, map `ValueGeneratedNever()`.

`User.cs`

```csharp
public sealed class User : AggregateRoot<UserId>
{
    private User() { }                    // EF only. No validation.

    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }

    public static OneOf<User, ValidationFailed> Create(
        string email, string passwordHash, TimeProvider clock)
    {
        var normalised = email.Trim().ToLowerInvariant();
        if (!IsWellFormedEmail(normalised))
            return new ValidationFailed("email", "Not a valid email address.");

        return new User
        {
            Id = UserId.New(),
            Email = normalised,
            PasswordHash = passwordHash,
            CreatedAt = clock.GetUtcNow(),
        };
    }
}
```

Three EF traps this shape avoids, each documented and each expensive:

1. **No constructor with matching parameter names.** EF's constructor binder is convention-based and accessibility-blind; `private User(UserId id, string email, …)` would be picked for materialisation and run your guards on every `SELECT`. Object-initialiser construction inside `Create` sidesteps it.
2. **No validation in setters.** `PropertyAccessMode.PreferField` has been the default since EF Core 3.0, so EF writes the backing field and never calls the setter. Validation there is dead code that looks alive.
3. **`TimeProvider` injected, not `DateTimeOffset.UtcNow`.** Makes `CreatedAt` assertable, and matches the Phase 3 poller which needs `FakeTimeProvider`.

`RefreshToken.cs` — `Id`, `UserId`, `TokenHash` (`byte[]`), `ExpiresAt`, `CreatedAt`, `SupersededAt`, `SupersededBy`, plus `Supersede(RefreshToken replacement, TimeProvider clock)` which **throws** if already superseded.

`User.ChangePasswordHash(string newHash)` **is built**, with tests. An earlier revision of this plan deferred it to Phase 5 on the grounds that "an untested public mutator is worse than none" — that objection dissolves once it is tested, and `identity-contracts.md` (which three agents built against) requires it. No endpoint calls it yet; the Phase 5 settings screen will.

`RefreshToken.Revoke(TimeProvider)` was **added** beyond the original design, and had to be: `RevokeSessionHandler` must end a session with *no* replacement, while `Supersede` requires one. Without it, logout could only be expressed as `token.Supersede(token, clock)` — a self-link that corrupts the rotation chain replay detection depends on.

### 5.3 `Identity.Application`

One folder per use case: command, result union, handler. No validators — those check the HTTP request and live in `.Presentation` (§4.5, §5.5).

```csharp
[GenerateOneOf]
public partial class RegisterResult
    : OneOfBase<TokenPair, EmailAlreadyUsed, ValidationFailed>;
```

**The refresh command is `RefreshSession(string RefreshToken)`, not `RefreshToken(string RefreshToken)`.** The design doc's name is **CS0542** — a positional record generates a member with the parameter's name, and a member cannot share the name of its enclosing type. It would also collide with the `RefreshToken` *entity* in `.Domain`, forcing `using` aliases in every file that touches both. Same for `RevokeSession`. Fix it in `phase-1-sign-in.md` §2.3 too.

`RegisterUserHandler`:

1. shape already validated by the endpoint filter — assume well-formed input
2. hash the password (`IPasswordHasher`)
3. `User.Create(...)` → propagate `ValidationFailed`
4. `IUserRepository.AddAsync(...)` → `AlreadyExists` → map to `EmailAlreadyUsed`

**The unique-violation catch belongs in the repository, not the handler.** Detecting SQLSTATE `23505` requires `Npgsql.PostgresException`, and `.Application` must not reference the driver. `UserRepository` (Infrastructure) catches `DbUpdateException`, inspects `PostgresException.SqlState`, and returns a provider-neutral result the handler maps. The *strategy* — rely on the unique index rather than check-then-insert, because check-then-insert is a race — is right and reappears in Phase 2 for `(user_id, ticker)` merges.

`LoginUserHandler` must run hash verification **even when the user does not exist**, against a fixed dummy hash, and return one undifferentiated `InvalidCredentials`. Two cases would leak account existence through both the response body and the response time.

### 5.4 `Identity.Infrastructure`

**`IdentityDbContext`**

```csharp
protected override void OnModelCreating(ModelBuilder b)
{
    b.HasDefaultSchema("identity");
    b.ApplyConfigurationsFromAssembly(
        typeof(IdentityDbContext).Assembly,
        predicate: t => t.Namespace!.StartsWith("StockPortfolio.Modules.Identity", StringComparison.Ordinal));
}

protected override void ConfigureConventions(ModelConfigurationBuilder c)
{
    c.Properties<UserId>().HaveConversion<UserIdConverter>();
    c.DefaultTypeMapping<UserId>().HasConversion<UserIdConverter>();
}
```

`UserIdConverter` derives from `ValueConverter<UserId, Guid>`, needs the EF reference, and therefore lives in `.Infrastructure` — **not** beside `UserId` in `.Domain`. That split is what stops EF leaking into the domain project.

The `DefaultTypeMapping` line is the one people miss: without it a `UserId` used anywhere other than a mapped entity property has no mapping and throws at runtime, long after model building succeeded.

Development only:

```csharp
.ConfigureWarnings(w => w.Throw(CoreEventId.SkippedEntityTypeConfigurationWarning))
```

`ApplyConfigurationsFromAssembly` silently skips any `IEntityTypeConfiguration` with constructor parameters, logging a warning nobody reads. Throwing in Development turns a mysteriously unmapped table into a startup failure.

**`DesignTimeIdentityDbContextFactory`** — `dotnet ef --startup-project src/Api` builds the host, which calls `UseNpgsql(config.GetConnectionString("Identity"))`. If that key is missing from `appsettings.Development.json`, `UseNpgsql(null)` throws at design time with an error naming neither the key nor the file. Ship an `IDesignTimeDbContextFactory` (or a dummy connection string) so migration commands never depend on local config.

**`Argon2PasswordHasher`** — Argon2id, OWASP `m=19456` / `t=2` / `p=1`, 16-byte salt from `RandomNumberGenerator`, 32-byte output, PHC-encoded so the parameters travel with the hash. There is no in-box Argon2 in .NET 10 and none planned, so the package is not a shortcut.

**`JwtTokenIssuer`** — `JsonWebTokenHandler`, not the legacy `JwtSecurityTokenHandler`. **Fail fast at startup** if the signing key is missing or under 32 bytes, not at first login. Refresh tokens are 32 random bytes, returned base64url, stored as a SHA-256 hash. SHA-256 without a work factor is correct precisely *because* the token is already high-entropy — Argon2 over a random 256-bit value buys nothing and costs 19 MiB per refresh.

### 5.5 `Identity.Presentation`

**`IdentityEndpoints.cs`**

```
POST /api/auth/register   201 + TokenPair | 409 | 400   filter: RegisterRequest
POST /api/auth/login      200 + TokenPair | 401         filter: LoginRequest
POST /api/auth/refresh    200 + TokenPair | 401         filter: RefreshRequest
POST /api/auth/logout     204                           .RequireAuthorization()
GET  /api/auth/me         200 + { id, email }           .RequireAuthorization()
```

Three of the five take a body, so three carry `.AddEndpointFilter<ValidationFilter<T>>()`. `/logout` and `/me` take nothing but a bearer token, so there is nothing to validate — do not add an empty validator for symmetry.

**`Validators/`** — one `AbstractValidator<T>` per request record. This is where "some logic" belongs: `RegisterRequestValidator` checks email shape, password length and character classes, and can express conditional or cross-field rules that DataAnnotations attributes cannot. Keep them free of I/O — "is this email already taken?" is a *context* question and belongs in the handler, where the answer is a `EmailAlreadyUsed` result case, not a 400.

Conventions, from the ASP.NET Core Web API guidance:

- request/response types are `sealed record` with `<summary>` XML doc comments — those flow into the OpenAPI document with no extra metadata calls
- `CancellationToken` in every signature, forwarded to every downstream call
- `TypedResults`, not `Results`, so OpenAPI infers response types
- `DateTimeOffset` for anything time-shaped
- `.WithName()` / `.WithSummary()` / `.Produces<T>(...)` chained on each endpoint
- **`.RequireAuthorization()`**, not `[Authorize]` — the attribute works on a lambda but reads as controller habit
- errors are RFC 7807 Problem Details from `AddProblemDetails()` plus the `IExceptionHandler`

On multi-case results, annotate the lambda with an explicit `Results<Ok<T>, ProblemHttpResult>` return type. `TypedResults.Ok(x)` and `TypedResults.Problem(...)` are unrelated types with no common base; without the annotation the compiler falls back to matching `RequestDelegate(HttpContext)` and reports the baffling `CS1593: delegate does not take N arguments`.

`POST /register` returns 201, which per HTTP semantics should carry a `Location`. There is no `GET /api/users/{id}` to point at, so either set `Location: /api/auth/me` or say in the README that the created resource is only addressable as the caller's own identity. An unexplained bare 201 reads as an oversight.

---

## 6. Host composition — `Api/Program.cs` order

```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Options, validated at startup
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration("Jwt").ValidateDataAnnotations().ValidateOnStart();

// 2. Cross-cutting
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();   // unhandled → ProblemDetails
builder.Services.AddOpenApi();                                  // NO Swashbuckle on .NET 9+
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.PropertyNameCaseInsensitive = false;
    o.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});

// 3. AuthN / AuthZ
builder.Services.AddStockPortfolioAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

// 4. CORS — see the note below before enabling this in Azure
builder.Services.AddCors(o => o.AddPolicy("spa", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
    .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

// 5. Modules — Infrastructure registers handlers, Presentation registers validators
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddIdentityPresentation();
builder.Services.DecorateHandlers();      // logging only; must come after the modules

// 6. Health
builder.Services.AddHealthChecks()
    .AddDbContextCheck<IdentityDbContext>("postgres")
    .AddCheck<RedisHealthCheck>("redis");

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("spa");            // must precede UseAuthentication
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment()) app.MapOpenApi();

app.MapIdentityEndpoints();
app.MapHealthChecks("/health/live",  new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program;      // required by WebApplicationFactory<Program>
```

Six things in there that are decisions, not boilerplate:

**No `UseResponseCompression()`.** Not now, not later. It buffers `text/event-stream` and Phase 4's alert feed dies silently — no error, just no events.

**Two health endpoints, and probes must be wired in Bicep or the split is inert.** `/health/live` checks nothing; `/health/ready` checks Postgres and Redis. Container Apps restarts a container failing liveness, so a liveness probe touching Postgres turns a database blip into a restart loop. **But** when ingress is enabled, ACA injects default **TCP** probes and never calls either path. The container app module must declare `probes: [{ type: 'Liveness', httpGet: { path: '/health/live', port: 8080 } }, { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 } }]`. Added to §12 step 14.

**Pick one CORS layer.** `phase-1-sign-in.md` §4 sets `ingress.corsPolicy.allowedOrigins` *and* this registers `UseCors`. Two layers on one response risks a duplicate `Access-Control-Allow-Origin`, which browsers reject outright. Ship the ASP.NET Core policy (it is testable locally and identical in compose) and leave the ACA `corsPolicy` unset — or the reverse, but not both untested.

**`MapInboundClaims = false` plus explicit `NameClaimType` / `RoleClaimType`** in `AddStockPortfolioAuthentication`. Verified: `JwtBearerOptions.MapInboundClaims` **defaults to `true`**, even though the underlying `JsonWebTokenHandler.MapInboundClaims` defaults to `false` — the options object overrides the handler. Leave it and `User.FindFirst("sub")` returns null forever, because the claim has been renamed to `http://schemas.xmlsoap.org/…/nameidentifier`.

**`JsonNumberHandling.Strict` stays, and money gets a converter.** CLAUDE.md requires money serialised as strings; from Phase 2 a purchase price must also be *read* from one, which `Strict` forbids for plain `decimal`. The answer is not to loosen the global option — it is a `MoneyJsonConverter` on the `Money` type, which bypasses `NumberHandling` entirely. `Quantity` stays a plain JSON number. Decided now so Phase 2 does not change a global option under time pressure.

**`public partial class Program;`** or every integration test fails to compile.

---

## 7. Persistence, roles and migrations

### 7.1 `db/init/` — a shell wrapper, not a bare `.sql`

The design's `CREATE ROLE migrator LOGIN PASSWORD :'migrator_pw';` **cannot work as a plain `.sql` file.** `docker-entrypoint-initdb.d` runs `.sql` files with `psql -v ON_ERROR_STOP=1 -f <file>` and passes **no** `-v` user variables, so `:'migrator_pw'` is a syntax error and, with `ON_ERROR_STOP=1`, aborts initialisation. `docker compose up` then fails from a clean clone — **P0 req 7**, the first thing a grader runs.

Ship two files. `00-roles.sh` supplies the variables; `01-roles.sql` stays the single source of truth so compose, Testcontainers and the Azure job all execute identical text:

```bash
#!/bin/bash
set -e
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  -v migrator_pw="$MIGRATOR_PW" \
  -v identity_pw="$IDENTITY_PW" \
  -v portfolio_pw="$PORTFOLIO_PW" \
  -v marketdata_pw="$MARKETDATA_PW" \
  -v alerts_pw="$ALERTS_PW" \
  -f /db/init/01-roles.sql          # ⚠️ NOT /docker-entrypoint-initdb.d/ — see below
```

⚠️ **`01-roles.sql` must not be visible inside `/docker-entrypoint-initdb.d`.** The entrypoint globs that directory once and runs *everything* in it — so if the `.sql` sits there too, it gets executed **bare** after the wrapper, with no `-v` flags, hitting exactly the syntax error the wrapper exists to prevent. Mount them separately:

```yaml
- ./db/init/00-roles.sh:/docker-entrypoint-initdb.d/00-roles.sh:ro
- ./db/init:/db/init:ro
```

⚠️ **`.gitattributes` must force `*.sh` to `eol=lf`.** With `core.autocrlf=true` on Windows — the default for many installs — a clean clone gives the wrapper a CRLF shebang, the container fails on `#!/bin/bash\r`, Postgres init aborts, and `docker compose up` dies. Same for Dockerfiles and `nginx.conf`. This is a P0 req 7 failure that only appears on someone else's machine.

⚠️ **`postgres:18` moved its default data directory** under `/var/lib/postgresql/<major>/`. A named volume mounted at the old `/var/lib/postgresql/data` therefore persists nothing, and `docker compose down && up` silently loses every account. Pin `PGDATA` to a subdirectory of the mount.

`01-roles.sql` creates all four schemas and all five roles **in Phase 1**, even though only `identity` has tables — `PortfolioRole_CannotReadIdentitySchema` needs `portfolio_svc` to exist, and that test is the whole point of the role design.

```sql
CREATE ROLE migrator      LOGIN PASSWORD :'migrator_pw';
CREATE ROLE identity_svc  LOGIN PASSWORD :'identity_pw';
-- portfolio_svc, marketdata_svc, alerts_svc

GRANT migrator TO CURRENT_USER;          -- ⚠️ required on Azure, harmless locally

CREATE SCHEMA identity AUTHORIZATION migrator;
-- portfolio, marketdata, alerts

REVOKE ALL ON SCHEMA identity FROM PUBLIC;
GRANT USAGE ON SCHEMA identity TO identity_svc;
ALTER DEFAULT PRIVILEGES FOR ROLE migrator IN SCHEMA identity
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO identity_svc;
-- same for the other three; no cross-grants anywhere
```

**`GRANT migrator TO CURRENT_USER` is not optional.** `CREATE SCHEMA … AUTHORIZATION migrator` and `ALTER DEFAULT PRIVILEGES FOR ROLE migrator` both require the executing role to be a *member of* `migrator`. In compose the entrypoint runs as superuser so it works; on Azure Postgres Flexible Server the admin account is **not** a superuser, and the migration job fails on first deploy — after everything looked fine locally.

**`ALTER DEFAULT PRIVILEGES FOR ROLE migrator`** is the other load-bearing clause. Grant on existing tables only and every future migration produces tables the service role cannot read — a failure that surfaces one phase later and looks like an EF bug.

**Never put `SearchPath=` in a connection string.** Two open Npgsql issues make it fail migrations with `42P07: relation "__EFMigrationsHistory" already exists`. Explicit `MigrationsHistoryTable` per context avoids the class.

Every connection string carries `Maximum Pool Size=2`. Four roles are four separate Npgsql pools because the `Username` differs; the default of 100 would request 800 against a B1ms allowing 35. `AddDbContextCheck` shares EF's pooling rather than opening a fifth pool — another reason it beats the community health-check package.

### 7.2 The `Migrator` project

The design doc says "runs an EF migrations bundle". Four contexts means four bundle executables and a shell script to sequence them.

**Use a console project instead.** It references every `<M>.Infrastructure`, resolves each `DbContext` and calls `Database.MigrateAsync()` in order, connecting as `migrator`. One image, one entrypoint, used identically by the compose `migrations` service and the ACA `job-migrate`, and it can log which contexts it applied.

Either way: the app itself must **never** call `Migrate()` at startup. Two replicas racing the same migration is a corrupted history table.

### 7.3 Migration commands

```bash
dotnet ef migrations add InitialIdentity \
  --context IdentityDbContext \
  --project src/Modules/Identity/StockPortfolio.Modules.Identity.Infrastructure \
  --startup-project src/Api \
  --output-dir Persistence/Migrations
```

---

## 8. Frontend — `src/Web`

```
src/Web/
  package.json
  vite.config.ts                 base from env — see below
  tsconfig.json                  TypeScript pinned; `latest` is now 7.0.2, the Go port
  index.html  nginx.conf  Dockerfile  .env.example
  src/
    main.tsx                     bootstrap-then-mount — see below
    index.css                    @import "tailwindcss"; @custom-variant dark
    routeTree.gen.ts             generated, committed
    routes/
      __root.tsx  index.tsx  login.tsx  register.tsx
      _authenticated.tsx         beforeLoad guard + AppShell
      _authenticated/dashboard.tsx     empty shell this phase
    lib/{apiClient,tokenStore,queryClient}.ts
    auth/{AuthProvider.tsx,useAuth.ts}
    components/{Button,TextField,Card,Alert,Spinner,AppShell}.tsx
  tests/{setup.ts,auth.test.tsx}
```

Five things that will be wrong if copied from a tutorial:

**`base` must come from the environment, not be hardcoded.** `base: process.env.VITE_BASE ?? '/'`, with a matching router `basepath`, and `VITE_BASE=/<repo>/` set only in the Pages job. Hardcoding `/<repo>/` means the compose SPA — served by nginx at `/` — requests `/tickerzone/assets/*.js` and renders a blank page. That is P0 req 7 failing on the grader's first command. Same shape for `VITE_API_BASE_URL`: empty under compose so `/api` goes through the nginx proxy.

**The session bootstrap must finish before the router mounts.** This is the single most likely way Phase 1 ships "done" and demos broken. The guard is synchronous —

```tsx
beforeLoad: ({ context }) => { if (!context.auth.isAuthenticated) throw redirect({ to: '/login' }) }
```

— and a React effect runs *after* first render, so on a hard refresh of `/dashboard` `beforeLoad` sees `isAuthenticated === false` and bounces to `/login`. That is exactly the P0 session-persistence criterion. Call `/api/auth/refresh` in `main.tsx` and render a splash until it settles, *then* mount `<RouterProvider>`; or hold the bootstrap promise in router context and `await` it in `beforeLoad`.

**Tailwind v4 has no config file.** `@tailwindcss/vite` plus `@import "tailwindcss"` in CSS. `darkMode: 'class'` does not exist; dark mode is `@custom-variant dark (&:where(.dark, .dark *))` in CSS. The failure is silent — `dark:` classes simply never apply and you assume the toggle is broken.

**Dedupe the refresh** with a single in-flight promise, or ten concurrent 401s fire ten refreshes and nine race a rotated token into failure. There is an MSW request-counter test for exactly this.

**React 19 StrictMode double-invokes effects.** No SSE yet, but the bootstrap effect needs a `cancelled` flag and cleanup now — the habit is what matters when Phase 4 adds the stream and the six-connections-per-origin limit starts to bite.

Session storage split, in the README because it differs by deployment:
- **access token in memory only** — module variable + context
- **compose**: refresh token in an httpOnly cookie, same origin through nginx
- **GitHub Pages**: origins differ, so the refresh token comes back in the body and lives in `sessionStorage`

That is a genuine weakening and the honest consequence of static hosting. Say so, and let it argue for a shorter refresh TTL (§11).

---

## 9. Containers

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
# Directory.*.props + global.json FIRST, then csproj files, then restore, then source
RUN dotnet publish src/Api/StockPortfolio.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "StockPortfolio.Api.dll"]
```

**`USER app` must be explicit.** The base image *creates* the `app` user but only `-chiseled` variants *set* it. Without the line you run as root.

**`EXPOSE 8080`**, and `targetPort: 8080` in Bicep. ASP.NET Core has listened on 8080 since .NET 8.

**Copy `Directory.Packages.props`, `Directory.Build.props` and `global.json` before the `.csproj` files** or restore fails inside the container with a CPM error that does not reproduce locally. Publish with an explicit `-o` because `ArtifactsPath` moved the default output.

`docker-compose.yml`:

```yaml
postgres:    healthcheck: ["CMD-SHELL", "pg_isready -U $$POSTGRES_USER -d $$POSTGRES_DB"]
redis:       command: redis-server --appendonly yes --appendfsync everysec
migrations:  depends_on: { postgres: { condition: service_healthy } }
api:         depends_on: { migrations: { condition: service_completed_successfully },
                           redis: { condition: service_started } }
web:         nginx, proxies /api → api
```

The healthcheck **must** pass `-U` and `-d`. A bare `pg_isready` reports healthy while `docker-entrypoint-initdb.d` is still running, and the migration container then connects to a database with no roles and fails in a way that looks intermittent.

nginx needs the SSE location block **now**, though nothing streams until Phase 4:

```nginx
location /api/alerts/stream {
    proxy_pass http://api;
    proxy_buffering off;
    gzip off;
    proxy_http_version 1.1;
    proxy_read_timeout 3600s;
}
```

nginx's default `proxy_read_timeout` is 60s — *stricter* than ACA's 240s, so the local stack would break before production does.

---

## 10. Tests

| Assembly | Depends on | Count in P1 |
|---|---|---|
| `Shared.Kernel.UnitTests` | nothing | ~3 |
| `Identity.UnitTests` | nothing | ~10 |
| `Api.IntegrationTests` | Testcontainers Postgres + Redis | ~10 |
| `Architecture.Tests` | reflection over all assemblies | 6 |

With Domain and Application public (§4.2), unit tests need no `InternalsVisibleTo`. `Api.IntegrationTests` exercises Infrastructure through HTTP, so its internals stay internal.

`Shared.Kernel.UnitTests` covers `Money` only. The design doc's `UserId_Converter_RoundTrips` and `UserId_New_ProducesSortableGuids` **move to `Identity.UnitTests`** — `UserId` is in `Identity.Domain` and its converter in `Identity.Infrastructure`, so they cannot live in an assembly that "depends on nothing".

**Integration fixture.** One Testcontainers collection fixture for the whole assembly — one Postgres container per run, not per class — running `01-roles.sql` as an init script so the isolation under test is the SQL that ships.

⚠️ **The fixture inherits the same init-script race the compose healthcheck warns about.** `Testcontainers.PostgreSql`'s default wait strategy reports ready before `docker-entrypoint-initdb.d` finishes, so a test connecting as `portfolio_svc` fails intermittently and looks flaky. Add an explicit wait probing `SELECT 1 FROM pg_roles WHERE rolname = 'portfolio_svc'`.

Two tests carry disproportionate weight:

**`PortfolioRole_CannotReadIdentitySchema`** — connect as `portfolio_svc`, select from `identity.users`, assert `PostgresException` with SQLSTATE **`42501`**. Assert the SQLSTATE, not the message; messages are localised and version-dependent. This converts a design claim into a fact CI re-checks.

**`Queries_NeverInlineUserInput_IntoCommandText`** — a `DbCommandInterceptor` registered in the fixture asserts no user-supplied value ever appears in `CommandText`. CLAUDE.md mandates it and the first draft of this plan omitted it. Brief **P0 req 6** is explicitly *«параметризація… конкатенація рядків у SQL неприпустима»*, and it is the one P0 item whose evidence is otherwise invisible to a reviewer reading EF LINQ.

**Deviation:** `Migrations_ApplyCleanly_OnEmptyDatabase` in the design doc asserts "all four contexts migrate; four schemas exist". In Phase 1 only `IdentityDbContext` exists. Assert four **schemas** (created by the init SQL) and one **context**; widen as phases 2–4 land.

`Architecture.Tests` uses plain reflection over `Assembly.GetReferencedAssemblies()` — no NetArchTest. Six rules, the last three new with the `.Presentation` split (§4.6, §4.7):

| Rule | Catches |
|---|---|
| No assembly references another module's non-`.Contracts` assembly | cross-module coupling |
| No `.Contracts` assembly references EF Core | persistence leaking across a boundary |
| No public settable property under `Modules.*.Domain` | anaemic entities |
| **No `.Infrastructure` references `Microsoft.AspNetCore.App`** | HTTP creeping back into persistence |
| **No `.Presentation` references EF Core or its own `.Infrastructure`** | a route reaching the database directly |
| **`Shared.Kernel` references nothing but `OneOf`** | the §4.7 bug returning |

Two that will fail on a naive implementation:

- The first rule must **exempt `Api` and `Migrator`** — they reference every `<M>.Infrastructure` and `<M>.Presentation` by design. Without the exemption, step 1 ends with a red test.
- The third must check `GetSetMethod(nonPublic: false) is not null`, or `private set` reads as a violation and every entity fails. This matters more now that Domain types are public.

`Identity.UnitTests` also gains validator tests — `RegisterRequestValidator` rejects a short password, accepts a good one. They touch no infrastructure, so they stay unit tests.

---

## 11. Your call — `TokenPolicy.cs`

The one place in Phase 1 where the design deliberately stops, because the answer is a security/UX judgement and it propagates straight into `Refresh_RotatesToken_OldOneRejected`.

The file will exist with the signature and this comment block. You write the four values (~8 lines):

```csharp
public static class TokenPolicy
{
    // TODO(you): access TTL, refresh TTL, whether refresh rotates on use, and the grace window.
    //
    //   Short access TTL   → smaller stolen-token window, more refresh round-trips.
    //   Rotate-on-use      → detects replay, but breaks concurrent tabs unless a superseded
    //                        token keeps working for a short grace period.
    //   Long refresh TTL   → fewer logins, larger blast radius if the token store leaks.
    //
    // This interacts with the GitHub Pages deployment: there the refresh token lives in
    // sessionStorage, not an httpOnly cookie, which argues for a shorter TTL than you would
    // otherwise pick.
    public static TimeSpan AccessTokenLifetime   => …;
    public static TimeSpan RefreshTokenLifetime  => …;
    public static bool     RotateOnUse           => …;
    public static TimeSpan RotationGracePeriod   => …;
}
```

Decide before writing the refresh integration test — the assertions encode the values.

---

## 12. Work order

| # | Step | Verified by |
|---|---|---|
| 1 | `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, solution, **all** project shells + references | `dotnet build` clean, `Architecture.Tests` compile |
| 2 | ~~Spike the OneOf toolchain~~ — **done**, see §3 | `[GenerateOneOf]` verified on Roslyn 5; no suppressor needed |
| 3 | `Shared.Kernel` + `Money` tests | green; no EF reference anywhere in the project |
| 4 | `Identity.Domain` — `AggregateRoot`, `UserId`, `User`, `RefreshToken` + tests | green; `User` does **not** re-declare `Id` |
| 5 | Argon2 hasher + PHC string + tests | round-trip and distinct-salt tests green |
| — | *half day* | |
| 6 | `IdentityDbContext`, configurations, converter, design-time factory; `00-roles.sh` + `01-roles.sql` | `dotnet ef migrations add InitialIdentity` succeeds with no local config |
| 7 | `Migrator` project | applies cleanly against a local Postgres container |
| 8 | Handlers + `IdentityModule` seam | unit tests green |
| 9 | `Shared.Presentation` (`ValidationFilter<T>`), then `Identity.Presentation` — endpoints, requests, validators | validator unit tests green |
| 9b | `Api/Program.cs`, JWT, Problem Details, health split, `.http` file | manual `.http` run: register → login → me → refresh; a bad email returns 400 `ValidationProblemDetails` |
| 10 | `Api.IntegrationTests` incl. role isolation **and the parameterisation interceptor** | `dotnet test` green |
| — | *one day* | |
| 11 | Dockerfiles, compose, nginx conf incl. the SSE block | `docker compose up` from a clean clone; `/health/ready` healthy |
| 12 | Vite + Tailwind v4 + routes + **bootstrap-then-mount** auth + `apiClient` | register/login in the browser; **hard-refresh keeps the session** |
| 13 | Vitest + MSW | `npm test` green incl. the single-refresh counter |
| 14 | Bicep incl. **explicit ACA liveness/readiness probes**, one CORS layer; `ci.yml`, `deploy.yml` | `bicep build` clean; `what-if` clean |
| 15 | Deploy; verify Pages → ACA and a deep link to `/login` | register and log in on the public URL |
| 16 | README: run instructions, token storage, role isolation, **SSE-vs-WS matrix**, accessibility trade-off | — |
| — | *1.25 days* | |

Steps 1–2 come before anything else because both set repo-wide switches that are expensive to reverse once forty files exist.

---

## 13. Risks and deviations, stated up front

**The brief lists WebSockets under the mandatory stack** («Реалтайм: WebSockets», §3 *Обов'язково*), and req 9 names WebSocket notifications. The plan ships SSE, justified by «Используй все что посчитаешь нужным» and a decision matrix. That is a reading of the task-giver's latitude, not a licence the brief itself grants — a strict grader could score it as a missed requirement. The decision stands; what Phase 1 must do is put the decision matrix in the README **during Phase 1**, not defer it to Phase 4, so the reasoning is in the repo from the first commit. Real-time is P1, so this cannot fail the P0 gate either way.

**Accessibility diverges from `CLAUDE.md`.** That file states "everything is `internal` outside `.Contracts`" as a non-negotiable; §4.2 replaces it with onion-per-module. **`CLAUDE.md` needs updating** or the next reader follows a rule the code does not obey. The cost of the change: cross-module isolation is now enforced by `Architecture.Tests`, not the compiler.

**Shape validation runs on the HTTP request, not the command** (§4.5). An `IEndpointFilter` in `.Presentation` returns 400 directly. The cost: a non-HTTP caller of a handler would bypass the rules. There is one caller per handler today, so it costs nothing now — but if a background job ever invokes a handler directly, its inputs need their own guard.

**`Identity.Contracts` ships empty.** Documented in the project's own README; evidence for the extraction-order argument.

**Five projects per module, twenty in total** (§4.6). The `.Presentation` split buys two compiler-enforced reference rules — Infrastructure never sees HTTP, Presentation never sees the database — at the cost of four extra `.csproj`. Reversed from an earlier draft that put endpoints in `.Infrastructure`; that draft was wrong about which layer inbound HTTP belongs to.

**Four schemas but one context in Phase 1.** Init SQL creates all four; only `identity` has tables.

**Redis is deployed in Phase 1 with no business consumer.** Nothing needs it until Phase 3, but it stays in compose and in `/health/ready` so the topology is real from day one and Phase 3 adds a client, not a service. The `RedisHealthCheck` and `IConnectionMultiplexer` registration are Phase 1 work — otherwise the §14 checklist item cannot be satisfied.

**Data Protection key persistence deferred to Phase 5.** Nothing in Phase 1 is DP-protected — no cookie auth, no antiforgery, no BYOK — so the key ring has no consumer. It *must* land with BYOK in Phase 5, or every ACA revision orphans stored ciphertext.

**`User.ChangePassword` is not built in Phase 1** — no caller, arrives in Phase 5.

**Package versions are pinned exactly**, no floating ranges, before the first commit anyone else clones.

---

## 14. Phase 1 exit checklist

- [ ] `docker compose up` from a clean clone → SPA loads **at `/`**, `/health/ready` healthy with Postgres and Redis
- [ ] Register → dashboard shell · **hard-refresh → still signed in** (bootstrap-then-mount, §8)
- [ ] Log out → `/dashboard` bounces to `/login?redirect=…` → log in → back to `/dashboard`
- [ ] `dotnet test` green, including `PortfolioRole_CannotReadIdentitySchema` **and `Queries_NeverInlineUserInput_IntoCommandText`**
- [ ] All six `Architecture.Tests` green — including `.Infrastructure` has no ASP.NET Core and `.Presentation` has no EF Core
- [ ] `POST /api/auth/register` with a malformed email returns 400 `application/problem+json` with a field-level error, produced by the filter and not by an exception
- [ ] `npm test` green, including the single-refresh counter
- [ ] `bicep build` and `az deployment group what-if` clean; **ACA HTTP probes declared**
- [ ] Pages URL talks to the ACA URL; deep link to `/login` loads (proves `404.html`)
- [ ] Exactly **one** CORS layer is active, and it is the tested one
- [x] OneOf toolchain verified on Roslyn 5 — no suppressor package needed (§3)
- [ ] README: run instructions, token storage, role isolation, SSE-vs-WebSockets matrix, accessibility trade-off
- [ ] `CLAUDE.md` accessibility rule updated to match §4.2
- [ ] Usable at 375px
- [ ] All package versions pinned exactly
- [ ] No `UseResponseCompression()` anywhere in the host
