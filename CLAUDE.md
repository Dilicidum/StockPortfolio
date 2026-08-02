# StockPortfolio

Stock-portfolio tracker: live quotes, P&L, threshold alerts over SSE. .NET 10 modular monolith + React SPA.

Built against a take-home brief (`TZ_Stock_Portfolio_App.docx`, Ukrainian). **P0 completion is the acceptance gate** — auth, quotes via TanStack Query, TanStack Router with 4+ routes, portfolio CRUD, dashboard with totals and P&L, parameterised DB access, and `docker compose up` bringing the whole stack up in one command. P1 and P2 add points; P0 failing means nothing else counts.

## Current state

**Phase 1 in progress.** The build foundation is in and green: 28 projects (`StockPortfolio.slnx`), `Directory.Build.props` / `.targets` / `Directory.Packages.props` with Central Package Management, `tests/Directory.Build.props`, and stub `Program.cs` files. `dotnet build` and `dotnet test` both pass. No feature code yet — every project is empty apart from the stubs.

Read before touching code: [docs/plan/00-overview.md](docs/plan/00-overview.md), then the phase file you're working in. Phase 1 additionally has [docs/plan/phase-1-implementation.md](docs/plan/phase-1-implementation.md) — the reviewed file-by-file build order; where it disagrees with `phase-1-sign-in.md`, the implementation plan wins. [docs/plan/er-diagram.md](docs/plan/er-diagram.md) and [docs/plan/module-interactions.md](docs/plan/module-interactions.md) are the reference diagrams. `docs/Initial.md` is the original architecture essay — **treat it as historical**; where it conflicts with `docs/plan/`, the plan wins, and three known errors in it are listed in the overview's open items.

Work phase by phase. A phase is done when it runs in a browser, not when tests pass.

## Commands

Land in Phase 1; until then they don't exist.

```bash
docker compose up                    # whole stack, from a clean clone, no API key needed
dotnet test                          # unit + integration (integration needs Docker running)
dotnet build
npm --prefix src/Web run dev
npm --prefix src/Web test
dotnet ef migrations add <Name> --context <Module>DbContext --project src/Modules/<M>/<M>.Infrastructure --startup-project src/Api
az deployment group what-if -g <rg> -f infra/main.bicep    # before any deploy
```

With no `Finnhub__ApiKey` configured the app uses `FakeQuoteProvider` and logs a warning. That is deliberate — Finnhub killed its sandbox in 2022, so the demo must work without a key.

## Architecture

Four modules — `Identity`, `Portfolio`, `MarketData`, `Alerts` — each with **five** projects: `.Contracts` / `.Domain` / `.Application` / `.Infrastructure` / `.Api`. Plus `Shared.Kernel`, `Shared.Api` and the `Api` host. Assembly and namespace prefix is `StockPortfolio.`; modules are `StockPortfolio.Modules.<Module>.<Layer>`.

**Accessibility follows the onion, not a blanket `internal`.** `internal` is per-assembly and a module is five assemblies, so "everything internal outside `.Contracts`" cannot compile — `Identity.Infrastructure` could not see `User` in `Identity.Domain`.

| Layer | Holds | Accessibility |
|---|---|---|
| `.Contracts` | records of primitives, for other modules | `public` |
| `.Domain` | entities, invariants | `public`, own module only |
| `.Application` | commands, results, handlers, abstractions | `public` |
| `.Infrastructure` | DbContext, repositories, hashing, tokens | **`internal`** except `<Module>Module` |
| `.Api` | endpoints, request/response records, validators | `public` (leaf project) |

Two reference rules are compiler-enforced and asserted by `Architecture.Tests`: **`.Infrastructure` never references ASP.NET Core; `.Api` never references EF Core or its own `.Infrastructure`.** They meet only through `.Application/Abstractions`.

- Inbound HTTP is presentation, not infrastructure. Do not move endpoints back into `.Infrastructure` (tried, wrong) or up into `Api` (makes the host the merge point for every feature).
- `Shared.Kernel` must stay framework-free — `AggregateRoot`, `Money`, `IDomainEvent`, the CQRS interfaces. Anything taking an `IEndpointRouteBuilder` goes in `Shared.Api`.
- A module references only other modules' `.Contracts`. The compiler no longer enforces this now that Domain is public, so `Architecture.Tests` is the enforcement and is load-bearing — do not weaken or skip it.
- `.Contracts` holds records of primitives only. No EF reference, no aggregates, no strongly-typed IDs — use raw `Guid`.
- Dependency direction is **Alerts → Portfolio → MarketData**. Identity has zero inbound runtime coupling; the JWT is self-contained. Keep it that way — it's the extraction-order argument.
- MarketData depends on nothing. It declares `IPollSetSource` and the host supplies an adapter over `Portfolio.Contracts`. Do not make MarketData read Portfolio directly.
- One `DbContext` and one Postgres schema per module, each connecting as its own role.

## Conventions

**CQRS without a dispatcher.** `ICommandHandler<,>` / `IQueryHandler<,>` injected straight into Minimal API endpoints. There is one caller per handler, so a mediator has nothing to decouple. Cross-cutting concerns are DI decorators.

**Results are `OneOf`** with `[GenerateOneOf]`, mapped to `TypedResults` via `.Match`. Exhaustiveness is structural: `.Match` takes one delegate per case, so adding a case breaks every call site. Never silence CS8509 with `_ => throw`, and never `switch` over `.Value` — that is the only way to lose the guarantee. No suppressor package is needed or installed.

**Rich domain.** Private setters, private parameterless EF constructor, static `Create(...)` returning a OneOf, instance methods enforcing invariants. `Id` is declared once on `AggregateRoot<TId>` and never re-declared on a derived entity (CS0108 is an error here).

**Validation has three layers, and only one uses result types.**

| Layer | Where | Mechanism |
|---|---|---|
| Shape — is this even an email? | FluentValidation on the request record, `.Api` | generic `ValidationFilter<T>` : `IEndpointFilter` returns **400** |
| Context — does this user exist? allowed? | handler, `.Application` | OneOf result case |
| Invariant — a User can never have a blank email | entity, `.Domain` | **throws** |

Shape validation is an **endpoint filter, not a DI decorator**. A decorator would have to return an unconstrained `TResult` and cannot manufacture a failure value; a filter sits in the HTTP pipeline and can `return TypedResults.ValidationProblem(...)` directly. Inject `IValidator<T>`, never `IEnumerable<IValidator<T>>` — the collection form silently validates nothing when a validator is missing. Validators do no I/O: "is this email taken?" is a context question and belongs in the handler as a result case. `LoggingDecorator` stays a decorator; it has no `TResult` problem. Do not use the built-in .NET 10 `AddValidation()` — it is DataAnnotations-attribute-driven and awkward for conditional or cross-field rules.

**Money is `decimal` server-side and serialised as strings.** Never compute money in the browser. Weight and percentages are computed server-side too.

**EF Core only — no raw SQL.** The brief permits raw or query builder and asks only for parameterisation, which EF Core makes structural. Parameterisation is proven by a `DbCommandInterceptor` in the test fixture asserting no user-supplied value ever reaches `CommandText`.

**Frontend: zero external UI component libraries.** No Radix, Headless UI or React Aria — the brief bans UI kits and its list ends in "тощо". Hand-build with Tailwind; use native `<select>` and `<input role="switch">`.

**Tests.** Unit tests touch no infrastructure. Integration tests share one Testcontainers collection fixture and need `public partial class Program { }`. Use `FakeTimeProvider` for anything timer-driven.

## Traps

Each of these costs a day if you meet it cold.

- **`HasDefaultSchema` does not move `__EFMigrationsHistory`** (efcore#24127, closed *not planned*). Every context needs `MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")` or all four share one table and corrupt each other's bookkeeping. Never put `SearchPath=` in a connection string.
- **A constructor whose parameter names match mapped properties gets hijacked by EF for materialisation**, running your guards on every `SELECT`. Binding is by convention and cannot be configured. Use a private parameterless constructor and a static factory.
- **`PropertyAccessMode.PreferField` is the default**, so EF writes the backing field and never calls your setter. Validation in a setter silently never runs.
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
- **`[GenerateOneOf]` crashes on types in the global namespace.** It derives the generated filename from the namespace and emits `<global namespace>_Foo.g.cs`; `<` is illegal, so the generator throws `CS8785` and every implicit conversion then fails with unrelated-looking errors. Always declare unions inside a namespace.
- **`OneOfDiagnosticSuppressor` does not exist on nuget.org** and is not needed. `.Match` takes one delegate per case, so exhaustiveness is enforced by arity — adding a case breaks every call site. `CS8509` only fires if you `switch` over `.Value`, which the convention forbids anyway.
- **`CA1707` makes every `Method_Scenario_Expectation` test a build error** under `TreatWarningsAsErrors`. `tests/Directory.Build.props` suppresses it — and must explicitly `<Import>` the root props, because MSBuild only auto-imports the first `Directory.Build.props` it finds walking up.
- **`GetPathOfFileAbove` inside `Exists(...)` fails to parse** with `MSB4092` — the nested single quotes break the condition parser. Hoist the path into a property first, then condition on the property.
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
