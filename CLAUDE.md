# StockPortfolio

Stock-portfolio tracker: live quotes, P&L, threshold alerts over SSE. .NET 10 modular monolith + React SPA.

Built against a take-home brief (`TZ_Stock_Portfolio_App.docx`, Ukrainian). **P0 completion is the acceptance gate** — auth, quotes via TanStack Query, TanStack Router with 4+ routes, portfolio CRUD, dashboard with totals and P&L, parameterised DB access, and `docker compose up` bringing the whole stack up in one command. P1 and P2 add points; P0 failing means nothing else counts.

## Current state

**Phase 1 is functionally complete.** 28 projects, `dotnet build` clean, 153 tests green, and `docker compose up` brings the whole stack up from a clean volume with register/login/refresh/logout verified in a browser. Outstanding: `TokenPolicy` values are provisional, `bicep build` has never run locally, nothing is deployed.

**`docs/plan/` predates the CQRS conventions below and has not been swept.** Its snippets still show `[GenerateOneOf]` union classes, `ValidationFailed`, `IUnitOfWork`, and commands bound straight off the wire — all four are now wrong. Where a plan file conflicts with the Conventions section here, **this file wins**; take the plan for its sequencing and its decisions, not its code.

Read before touching code: [docs/plan/00-overview.md](docs/plan/00-overview.md), then the phase file you're working in. Phase 1 additionally has [docs/plan/phase-1-implementation.md](docs/plan/phase-1-implementation.md) — the reviewed file-by-file build order; where it disagrees with `phase-1-sign-in.md`, the implementation plan wins. [docs/plan/er-diagram.md](docs/plan/er-diagram.md) and [docs/plan/module-interactions.md](docs/plan/module-interactions.md) are the reference diagrams. `docs/Initial.md` is the original architecture essay — **treat it as historical**; where it conflicts with `docs/plan/`, the plan wins, and three known errors in it are listed in the overview's open items.

Work phase by phase. A phase is done when it runs in a browser, not when tests pass.

## Commands

```bash
docker compose up                    # whole stack, from a clean clone, no API key needed
dotnet build
dotnet test                          # 153 tests; the integration suite needs Docker running
npm --prefix src/Web run dev
npm --prefix src/Web test

# migrations: --project is the module's Infrastructure, --startup-project is always the host
dotnet ef migrations add <Name> --context <Module>DbContext --output-dir Persistence/Migrations \
  --project src/Modules/<M>/StockPortfolio.Modules.<M>.Infrastructure --startup-project src/Api

az deployment group what-if -g <rg> -f infra/main.bicep    # before any deploy
```

`/openapi/v1.json` is served in Development only, so read it from `dotnet run --project src/Api`, not from the container.

With no `Finnhub__ApiKey` configured the app uses `FakeQuoteProvider` and logs a warning. That is deliberate — Finnhub killed its sandbox in 2022, so the demo must work without a key.

## Architecture

Four modules — `Identity`, `Portfolio`, `MarketData`, `Alerts` — each with **five** projects: `.Contracts` / `.Domain` / `.Application` / `.Infrastructure` / `.Api`. Plus `Shared.Kernel`, `Shared.Api`, the `Api` host and a `Migrator` console. Assembly and namespace prefix is `StockPortfolio.`; modules are `StockPortfolio.Modules.<Module>.<Layer>`.

**Accessibility follows the onion, not a blanket `internal`.** `internal` is per-assembly and a module is five assemblies, so "everything internal outside `.Contracts`" cannot compile — `Identity.Infrastructure` could not see `User` in `Identity.Domain`.

| Layer | Holds | Accessibility |
|---|---|---|
| `.Contracts` | records of primitives, for other modules | `public` |
| `.Domain` | entities, invariants | `public`, own module only |
| `.Application` | commands, results, handlers, abstractions | `public` |
| `.Infrastructure` | DbContext, repositories, hashing, tokens | **`internal`** except `<Module>Module` |
| `.Api` | endpoints, request records, validators | `public` (leaf project) |

Two reference rules are compiler-enforced and asserted by `Architecture.Tests`: **`.Infrastructure` never references ASP.NET Core; `.Api` never references EF Core or its own `.Infrastructure`.** They meet only through `.Application/Abstractions`.

- Inbound HTTP is presentation, not infrastructure. Do not move endpoints back into `.Infrastructure` (tried, wrong) or up into the **`Api` host** (makes the host the merge point for every feature). `StockPortfolio.Api` is the host; `StockPortfolio.Modules.<M>.Api` is a module's HTTP layer — different assemblies, no collision.
- `Shared.Kernel` must stay framework-free — `Money` and the CQRS interfaces, nothing else. There is no `AggregateRoot` and no `IDomainEvent`; both were deleted as unused, and Phase 4 adds an event type where one is actually raised. Anything taking an `IEndpointRouteBuilder` goes in `Shared.Api`.
- A module references only other modules' `.Contracts`. The compiler no longer enforces this now that Domain is public, so `Architecture.Tests` is the enforcement and is load-bearing — do not weaken or skip it.
- `.Contracts` holds records of primitives only. No EF reference, no aggregates, no strongly-typed IDs — use raw `Guid`.
- Dependency direction is **Alerts → Portfolio → MarketData**. Identity has zero inbound runtime coupling; the JWT is self-contained. Keep it that way — it's the extraction-order argument.
- MarketData depends on nothing. It declares `IPollSetSource` and the host supplies an adapter over `Portfolio.Contracts`. Do not make MarketData read Portfolio directly.
- One `DbContext` and one Postgres schema per module, each connecting as its own role.

## Conventions

**CQRS without a dispatcher.** `ICommandHandler<,>` / `IQueryHandler<,>` injected straight into Minimal API endpoints. There is one caller per handler, so a mediator has nothing to decouple. Cross-cutting concerns are DI decorators.

**CQRS layout and naming — both are fixed.**

```
Application/
  <FeatureArea>/            e.g. Authentication
    Commands/
      <UseCase>/            e.g. RegisterUser
        <UseCase>Command.cs
        <UseCase>CommandHandler.cs
        <UseCase>Result.cs        the SUCCESS shape, only if the use case needs its own
        <Failure>.cs              e.g. EmailAlreadyUsed - one file per failure case
    Queries/
      <UseCase>/
        <UseCase>Query.cs
        <UseCase>QueryHandler.cs
        <UseCase>Result.cs
```

Every CQRS type in a module lives under one feature-area folder, split into `Commands/` and `Queries/`, then one folder per use case. Class names carry the role: `RegisterUserCommand`, `RegisterUserCommandHandler`, `GetCurrentUserQuery`, `GetCurrentUserQueryHandler`. Never `RegisterUser` for a command or `RegisterUserHandler` for a handler — the suffix is not optional.

The namespace matches the folder, so it stops at the use-case folder (`…Application.Authentication.Commands.RegisterUser`) and does **not** repeat the `Command` suffix.

**Requests live in `.Api`, not `.Application`.** An Application type never binds off the wire. `.Api/Requests/<UseCase>Request.cs` is the request body; the endpoint reads it and constructs the command with `new`. The validator sits beside it as `<UseCase>RequestValidator` and validates the **request**, so `ValidationFilter<T>` closes over the request type. The two records look alike today and that is fine — the wire contract and the use-case input are free to move apart, and only the request appears in `/openapi/v1.json`.

**A handler returns `OneOf<…>` of its outcomes directly.** No `[GenerateOneOf]`, no named union class — `Task<OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>>` says at the signature what a wrapper would hide behind a name, and the DI registration and the endpoint's injected `ICommandHandler<,>` both spell out the same closed generic. `<UseCase>Result` is the **success payload record**, never the union. Failure cases are records beside the use case that returns them, not pooled in an `Errors.cs`; `InvalidInput` (in `Shared.Kernel`) is the one shared failure, because every layer can produce a field-plus-message.

Map to `TypedResults` via `.Match`. Exhaustiveness is structural: `.Match` takes one delegate per case, so adding a case breaks every call site. Never silence CS8509 with `_ => throw`, and never `switch` over `.Value` — that is the only way to lose the guarantee. No suppressor package is needed or installed. **Name every `.Match` lambda parameter** — `emailTaken =>`, not `_ =>`. The discard costs the one word that says which case this is.

**Rich domain, and no base class.** There is no `AggregateRoot<TId>`; each entity declares its own `Id`. An entity has **exactly one constructor**: private, taking every mapped value, assigning and nothing else. No parameterless constructor, no object initialiser, no public setter — a half-built entity is not representable, and the static `Create(...)` returning a OneOf is the only way in.

The constructor must stay guard-free. EF's binder matches on parameter name and *will* select it for materialisation, so anything it does runs on every row of every `SELECT`. That is the real trap — not the constructor itself. Validation lives in the factory, which EF never calls.

**Use `OneOf.Types.Success` and `OneOf.Types.NotFound`.** They ship with the package. Do not redeclare them per module, and never put a `Success` type in an errors file.

**No unit of work, and no `ConfigureAwait(false)`.** `DbContext` already *is* a unit of work, so a second one over it only adds a name; a repository write method persists before it returns, and because every repository in a module shares one scoped `DbContext`, one commit carries whatever else the handler changed. Where two writes must land together, order them so the last repository call is the one that commits. `ConfigureAwait(false)` is for general-purpose libraries — ASP.NET Core has no `SynchronizationContext`, so it is noise here.

**No defensive null checks where the compiler already answered.** `ArgumentNullException.ThrowIfNull(app)` in an endpoint-mapping extension, or `ThrowIfNull(command)` in a handler the API constructs the command for, guards against a caller that cannot exist. Keep the guards on genuinely public seams that take user-shaped input.

**Validation has three layers, and only one uses result types.**

| Layer | Where | Mechanism |
|---|---|---|
| Shape — is this even an email? | FluentValidation on the **request**, `.Api` | generic `ValidationFilter<T>` : `IEndpointFilter` returns **400** |
| Context — does this user exist? allowed? | handler, `.Application` | OneOf result case |
| Invariant — a User can never have a blank email | entity, `.Domain` | **throws** |

**"Is this address taken?" is a context question, so the handler asks it.** `RegisterUserCommandHandler` does a `FindByEmailAsync` and returns `EmailAlreadyUsed`; it does not insert and read a unique-violation back out of the exception. The look-up is normalised through `User.NormaliseEmail` — the one place the canonical form is defined, used by `User.Create` on the way in and by both handlers on the way out, because a lookup that normalises differently from what was stored simply misses.

Known and accepted: two simultaneous registrations of one address can both pass the check, and the loser then hits the unique index and surfaces as **500 rather than 409**. The index stays — it is what keeps the data correct — and the race is a millisecond wide. Reintroduce a `DbUpdateException` catch only if that 500 ever actually shows up.

Shape validation is an **endpoint filter, not a DI decorator**. A decorator would have to return an unconstrained `TResult` and cannot manufacture a failure value; a filter sits in the HTTP pipeline and can `return TypedResults.ValidationProblem(...)` directly. Inject `IValidator<T>`, never `IEnumerable<IValidator<T>>` — the collection form silently validates nothing when a validator is missing. Validators do no I/O: "is this email taken?" is a context question and belongs in the handler as a result case. `LoggingDecorator` stays a decorator; it has no `TResult` problem. Do not use the built-in .NET 10 `AddValidation()` — it is DataAnnotations-attribute-driven and awkward for conditional or cross-field rules.

**Endpoint handlers return `Task<IResult>`.** Not `Task<Results<Created<T>, ProblemHttpResult, …>>` — the typed union restates in the signature what `.Produces(...)` already declares, and it grows a generic argument every time a case is added. `.Match<IResult>(...)` keeps the exhaustiveness, since that comes from the union's arity, not from the return type.

The trade is real and worth knowing: the typed union made the compiler reject a result the signature had not declared. `.Produces(...)` metadata is now the **only** description of what a route emits, so it can drift from the code silently. That is why the next rule says verify against a live response — this change was itself made by diffing `/openapi/v1.json` before and after and confirming the document was byte-identical.

**Every endpoint declares every status it can emit.** `.Produces<T>(200)` for the success shape, then `.ProducesValidationProblem()` (400), plus `.ProducesProblem(...)` for each of 401 / 409 / 415 / 500 that the route can actually reach. `ProducesValidationProblem()` is metadata only — it documents the 400 that `ValidationFilter<T>` returns and is shorthand for `Produces<HttpValidationProblemDetails>(400, "application/problem+json")`.

415 and 500 are declared as `problem+json` because `AddProblemDetails()` **and** `UseStatusCodePages()` are both registered, which is what gives framework-generated bare status codes a body. Drop either one and those declarations become lies. Verify with a real request before adding a status — a bare `Produces` claiming a body that never arrives is worse than an undeclared response. Read the result back from `/openapi/v1.json`, not from the source.

**Comments: one line, and only where the code cannot say it.** No `<remarks>`, no `<param>`/`<returns>`/`<exception>` blocks, no banner rules. A doc comment is a single `/// <summary>…</summary>`. If a comment must span lines to make sense, the rationale belongs in `docs/plan/` or a commit message, not in the file.

**Money is `decimal` server-side and serialised as strings.** Never compute money in the browser. Weight and percentages are computed server-side too.

**EF Core only — no raw SQL.** The brief permits raw or query builder and asks only for parameterisation, which EF Core makes structural. Parameterisation is proven by a `DbCommandInterceptor` in the test fixture asserting no user-supplied value ever reaches `CommandText`.

**Frontend: zero external UI component libraries.** No Radix, Headless UI or React Aria — the brief bans UI kits and its list ends in "тощо". Hand-build with Tailwind; use native `<select>` and `<input role="switch">`.

**Tests.** 153 of them: unit (touch no infrastructure), architecture (reflection over assembly references), integration (Testcontainers Postgres + Redis, one collection fixture for the assembly, needs `public partial class Program;`). Use `FakeTimeProvider` for anything timer-driven.

**A test that cannot fail is worse than no test**, because it reads as enforcement. Every architecture rule was verified by deliberately breaking it and watching it go red — that is how `PresentationAssemblies => AssembliesFor("Infrastructure")` was found, a copy-paste that pointed one rule at the wrong layer while reporting green. `ReferenceWalker_FindsEdgesThatDoExist` guards the same class of bug permanently: rules that pass by finding nothing need a companion that fails if the search finds nothing.

## Traps

Each of these costs a day if you meet it cold.

- **`HasDefaultSchema` does not move `__EFMigrationsHistory`** (efcore#24127, closed *not planned*). Every context needs `MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")` or all four share one table and corrupt each other's bookkeeping. Never put `SearchPath=` in a connection string.
- **A constructor whose parameter names match mapped properties gets hijacked by EF for materialisation.** Binding is by convention and cannot be configured. This is *fine and intended* here — the single private all-args constructor only assigns. It becomes a trap the moment a guard is added inside it, because EF then re-runs that guard on every row of every `SELECT`. Guards belong in the static factory.
- **`PropertyAccessMode.PreferField` is the default**, so EF writes the backing field and never calls your setter. Validation in a setter silently never runs — which is moot now that entities have no settable surface, but it is why they don't.
- **`Maximum Pool Size=2` on every connection string.** Azure Postgres B1ms allows 35 user connections and a different username is a different Npgsql pool; the default of 100 × 4 roles × 2 replicas requests 800. PgBouncer is unavailable on Burstable.
- **An unhandled exception in a `BackgroundService` kills the host** (`StopHost` is the default). The poll loop needs an in-loop `try/catch`.
- **Assigning a `DelayGenerator` silently disables `Retry-After` handling**, which is honoured by default.
- **Never add `UseResponseCompression()`** — it buffers `text/event-stream` and the alert feed dies silently.
- **ACA `requestIdleTimeout` is 4 minutes and 4 is the floor** on Consumption. The SSE heartbeat must fire every 20s. `SseFormatter` has no comment API, so use a named `ping` event.
- **ACA liveness must not check Postgres or Redis.** A dependency blip then becomes a container restart loop, turning a degraded app into a down one.
- **TanStack Query v5.89.0 changed every mutation callback signature** (`onMutateResult` inserted before a new `context`). Every optimistic-update tutorial written before Sept 2025 rolls back the wrong snapshot.
- **Tailwind v4 has no config file.** `darkMode: 'class'` does not exist; dark mode is `@custom-variant` in CSS. The failure is silent — `dark:` classes just never apply.
- **Data Protection keys must be persisted to Postgres** or every ACA revision orphans stored BYOK ciphertext.
- **ASP.NET Core listens on 8080**, not 80. `targetPort: 8080` in Bicep.
- **React 19 StrictMode double-invokes effects.** The SSE hook needs a `cancelled` flag and `clearTimeout` in cleanup, or you hold two of the browser's six connections per origin.
- **`docker-entrypoint-initdb.d` passes no `-v` to psql.** A `.sql` file using `:'password'` variables is a syntax error and, with `ON_ERROR_STOP=1`, aborts init — so `docker compose up` fails from a clean clone. Wrap it in a `.sh` that supplies the variables.
- **`CREATE SCHEMA … AUTHORIZATION migrator` needs `GRANT migrator TO CURRENT_USER` first.** Compose runs as superuser so it passes locally; the Azure Flexible Server admin is not a superuser and the migration job fails on first deploy.
- **`beforeLoad` is synchronous; React effects run after first render.** Bootstrap the session *before* mounting `RouterProvider`, or a hard refresh of a guarded route always bounces to `/login` — which is the session-persistence requirement failing while every test passes.
- **Vite `base` must come from the environment**, not be hardcoded to `/<repo>/`. nginx serves the compose SPA at `/`, so a baked-in base makes it request `/<repo>/assets/*.js` and render blank.
- **ACA injects default TCP probes when ingress is on.** Declare `httpGet` liveness/readiness probes in Bicep or `/health/live` and `/health/ready` are never called and the split is decorative.
- **`OneOf.Types.NotFound` collides with `Microsoft.AspNetCore.Http.HttpResults.NotFound`.** Only bites a file that imports `HttpResults`, which endpoints no longer need now that they return `Task<IResult>`. If one ever does, alias it — `using NotFound = OneOf.Types.NotFound;` — rather than fully qualifying at each use.
- **`[GenerateOneOf]` crashes on types in the global namespace.** It derives the generated filename from the namespace and emits `<global namespace>_Foo.g.cs`; `<` is illegal, so the generator throws `CS8785` and every implicit conversion then fails with unrelated-looking errors. Nothing uses the attribute now — handlers return `OneOf<…>` directly — but if one is ever reintroduced, declare it inside a namespace.
- **Revoking and rotating a refresh token are not the same end.** Both stamp `SupersededAt`; only rotation sets `SupersededBy`. A grace-period check written against `SupersededAt` alone therefore keeps accepting a token the user just logged out with, for the whole window — logout silently does nothing for 30 seconds while every test stays green. `Refresh_AfterLogout_IsRejectedInsideTheGraceWindow` pins it.
- **`OneOfDiagnosticSuppressor` does not exist on nuget.org** and is not needed. `.Match` takes one delegate per case, so exhaustiveness is enforced by arity — adding a case breaks every call site. `CS8509` only fires if you `switch` over `.Value`, which the convention forbids anyway.
- **`CA1707` makes every `Method_Scenario_Expectation` test a build error** under `TreatWarningsAsErrors`. `tests/Directory.Build.props` suppresses it — and must explicitly `<Import>` the root props, because MSBuild only auto-imports the first `Directory.Build.props` it finds walking up.
- **`GetPathOfFileAbove` inside `Exists(...)` fails to parse** with `MSB4092` — the nested single quotes break the condition parser. Hoist the path into a property first, then condition on the property.
- **EF needs no parameterless constructor — but it binds by NAME.** Constructor binding has existed since EF Core 2.1 and does not care about accessibility, so one private all-args constructor is enough. The hazard is renaming a constructor parameter without renaming its property: EF then finds no bindable constructor, and with no parameterless fallback the **whole model fails to build at startup**, not on the first query. `EfConstructorBindingTests` pins it.
- **An assembly-level `[SuppressMessage]` must live in its own `AssemblyInfo.cs`.** Twice now, deleting an unrelated type (`AggregateRoot.cs`, then `IEndpointModule.cs`) took the assembly's `CA1716` suppression with it and broke the build, because the attribute was riding on whichever file happened to be first.
- **`dotnet test --no-build` after a FAILED build silently runs the previous assembly** and reports green. A mutation test "passing" is meaningless unless the build that preceded it succeeded — check the build result, not just the test result.
- **Regex renames rewrite more than types.** Renaming `RefreshSession` → `RefreshSessionCommand` across the repo also rewrote the *namespace* segment and an OpenAPI operation id in a string literal. The namespace matches the **folder** and carries no role suffix; string literals are not identifiers. The build failed on two unrelated-looking XML `cref` errors, two steps from the cause.
- **Extension-filtered scripts miss `Dockerfile`** — it has no extension. A repo-wide rename left both .NET images copying `*.Presentation.csproj`; `dotnet build` stayed green because those paths only exist inside the container build context.
- **A local `dotnet run` holds file locks** and breaks the next build with `MSB3021: being used by another process`. `pkill` does not reach it on Windows — use `Stop-Process`.
- **Windows Application Control can block a freshly built DLL** with `0x800711C7 — An Application Control policy has blocked this file`, which surfaces as a `FileLoadException` in the architecture tests. Not a code fault; delete that project's `artifacts/bin` and `artifacts/obj` and rebuild.
- **`Microsoft.OpenApi` must stay on 2.x.** 2.0.0 carries GHSA-v5pm-xwqc-g5wc so pin ≥2.11.0, but 3.x makes `IOpenApiMediaType.Example` read-only while the ASP.NET Core OpenAPI source generator still assigns to it (`CS0200`).

## Deployment

Three targets: `docker compose` (whole stack, local, the P0 gate), **GitHub Pages** (SPA, static, `VITE_API_BASE_URL` baked in at build), **Azure Container Apps** (API only, `minReplicas: 1` or ingestion stops). Postgres Flexible B1ms and Azure Managed Redis Balanced B0 with HA off — **not** Azure Cache for Redis, which is retiring.

Cross-origin is permanent, so the SSE endpoint uses a single-use 30-second ticket rather than a header. GitHub Pages needs `404.html` copied from `index.html` plus a Vite `base` and matching router `basepath`.

## Deliberately not built

These were considered and cut. Don't reintroduce them without asking.

- **Alert replay** — no cursor, no `Last-Event-ID`, no 24h backfill. Req 9 asks for an event on breach, a background check, and a simulate button. History is a plain `GET`; the stream hook invalidates the query on reconnect.
- **Watchlist** — «перелік акцій» in req 8 sits inside *dashboard settings*, so it means which of your holdings show on the dashboard. That's `is_visible` on `holdings`.
- **A cached ticker table in MarketData** — the poll set is read live from Portfolio each cycle. Removing it also removed two event handlers, a reconciliation pass and a divergence failure mode.
- **Raw SQL** — see Conventions.
- **Trading-hours gating** — ships as a config flag defaulting to off. Read-through covers the weekend demo case.
- **WebSockets and SignalR** — SSE is the transport. The README carries the decision matrix; the UI badge says "Live (SSE)", never "WS Live".
