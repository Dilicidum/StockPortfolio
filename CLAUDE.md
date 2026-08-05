# StockPortfolio

Stock-portfolio tracker: live quotes, P&L, threshold alerts over SSE. .NET 10 modular monolith + React SPA.

Built against a take-home brief (`TZ_Stock_Portfolio_App.docx`, Ukrainian). **P0 completion is the acceptance gate** — auth, quotes via TanStack Query, TanStack Router with 4+ routes, portfolio CRUD, dashboard with totals and P&L, parameterised DB access, and `docker compose up` bringing the whole stack up in one command. P1 and P2 add points; P0 failing means nothing else counts.

## Current state

**Phases 1–3 are functionally complete.** **25 projects**, `dotnet build` clean at 0 warnings, and `docker compose up` brings the whole stack up from a clean volume. Phase 3 shipped MarketData end to end — the dashboard, its P&L arithmetic, the last-known-price fallback and the symbol-existence check. Outstanding: `TokenPolicy` values are provisional, and **Phase 3 is not deployed** — the live Azure deployment still serves pre-Phase-3 code, confirmed 2026-08-05 by `/api/dashboard` and `/api/marketdata/health` both returning **404** on the public FQDN while `/health/ready` returns 200. Phase 3's Bicep delta was expected to be zero lines and is zero lines, but *expected* is not *verified*.

⚠️ **This paragraph used to say "nothing is deployed", and that was false for three days.** There has been a live, healthy deployment since 2026-08-02 — the same sentence's own Deployment section below names its resource group. The reason the contradiction survived is that the record of that deploy lives in [docs/superpowers/specs/2026-08-02-azure-deployment-design.md](docs/superpowers/specs/2026-08-02-azure-deployment-design.md), which **nothing linked to**: the file's only inbound reference was inside itself. It is now linked from here and from Deployment below. An unreferenced document is one nobody reads, including the next agent. This paragraph used to quote a test count as well; it no longer does, because three copies of one number drifted apart. **Counts live in Tests below and nowhere else.**

**The module count moved twice and only one of the moves is on disk.** Phase 2 merged Alerts into Portfolio, taking four modules to three and 28 projects to 23; that merge was then **reversed as a decision** — the argument is in Architecture below — but no Alerts module was ever built, so `src/Modules/` still holds exactly **three**: `Identity`, `Portfolio`, `MarketData`. Alerts is Phase 4's first task, and `ModuleBoundaryTests.cs`'s `ShouldBe(17, …)` (three modules × five layers, plus `Shared.Kernel` and `Shared.Api`) is what pins the gap — it becomes 22 the day Alerts lands. The database and deployment cleanup deferred with the merge is still outstanding and is in [docs/deferred-work.md](docs/deferred-work.md) as **E1, reopened**.

`docs/plan/` was swept to match the conventions below — its snippets are current, and where a decision was reversed the plan says so and why, rather than quietly showing the new shape. `docs/Initial.md` is the exception and stays historical.

**Read before touching anything operational** — deploys, Bicep, workflows, cost, teardown:

- [docs/DEPLOYING.md](docs/DEPLOYING.md) — **the runbook. Start here.** How to deploy (push to `main`, and nothing else — do not run `az deployment group create` by hand), what exists, how to verify, the cost ceiling, and the five traps that each cost a deploy cycle.
- [docs/superpowers/specs/2026-08-02-azure-deployment-design.md](docs/superpowers/specs/2026-08-02-azure-deployment-design.md) — the **why**: the cost model, the four decisions behind `minReplicas: 0` and the time-bounded ceiling, the six-step verification, and the six failed attempts in full. It sits outside `docs/plan/` on purpose, because `docs/plan/` is the numbered product build and this cuts across it.

Four of their traps are copied into Traps below; the rest are only there.

⚠️ **`DEPLOYING.md` was written on the `phase-2` branch and never merged**, so for three days the repo's only deploy runbook was invisible from `main` and from every later branch. It is carried into Phase 3 here. Operational documents strand more easily than code does — nothing fails to compile when they are missing.

Read before touching code: [docs/plan/00-overview.md](docs/plan/00-overview.md), then the phase file you're working in. Phase 1 additionally has [docs/plan/phase-1-implementation.md](docs/plan/phase-1-implementation.md) — the reviewed file-by-file build order; where it disagrees with `phase-1-sign-in.md`, the implementation plan wins. [docs/plan/er-diagram.md](docs/plan/er-diagram.md) and [docs/plan/module-interactions.md](docs/plan/module-interactions.md) are the reference diagrams. `docs/Initial.md` is the original architecture essay — **treat it as historical**; where it conflicts with `docs/plan/`, the plan wins, and three known errors in it are listed in the overview's open items.

Work phase by phase. A phase is done when it runs in a browser, not when tests pass.

## Commands

```bash
docker compose up                    # whole stack, from a clean clone, no API key needed
dotnet build
dotnet test                          # the integration suite needs Docker running; counts are in Tests below
npm --prefix src/Web run dev
npm --prefix src/Web test

# migrations: --project is the module's Infrastructure, --startup-project is always the host
dotnet ef migrations add <Name> --context <Module>DbContext --output-dir Persistence/Migrations \
  --project src/Modules/<M>/StockPortfolio.Modules.<M>.Infrastructure --startup-project src/Api

az deployment group what-if -g <rg> -f infra/main.bicep    # before any deploy
```

`/openapi/v1.json` is served in Development only — which `docker-compose.override.yml` makes the default, so it *is* reachable at `:8080` on a plain `docker compose up`, as well as from `dotnet run --project src/Api`.

With no `Finnhub__ApiKey` configured the app uses `FakeQuoteProvider` and logs a warning. That is deliberate — Finnhub killed its sandbox in 2022, so the demo must work without a key.

## Architecture

Four modules are *designed* — `Identity`, `Portfolio`, `MarketData`, `Alerts` — each with **five** projects: `.Contracts` / `.Domain` / `.Application` / `.Infrastructure` / `.Api`. **Three exist**; Alerts is Phase 4 and has no folder, no `.csproj` and no assembly today. Plus `Shared.Kernel`, `Shared.Api`, the `Api` host and a `Migrator` console. Assembly and namespace prefix is `StockPortfolio.`; modules are `StockPortfolio.Modules.<Module>.<Layer>`.

**Boundaries are argued from extraction cost, not from subdomain labels.** The test for every seam: would it survive becoming a network call? Four questions — does anything need a transaction across it, is the chattiness bounded, can one side fail while the other degrades, is there exactly one writer per table. Full reasoning in [docs/plan/module-boundaries.md](docs/plan/module-boundaries.md).

- **Alerts was merged into Portfolio in Phase 2 and that was reversed.** The merge argued that `Ticker` means the same thing on both sides, therefore one bounded context. That inverts the heuristic: language divergence is *sufficient* to conclude two contexts exist, not *necessary*. Two contexts can share a vocabulary entirely and still be two.
- `AlertSettings` and `FiredAlert` never share a transaction with `Holding`, no invariant spans any two of the three aggregates, they are written on a different trigger, and alerts can be down while the dashboard renders.
- **Core / supporting / generic subdomain classification is not used.** It is real DDD, it changed no code here, and it conflated three things: a subdomain is problem space, a bounded context is a model boundary, and a module in Evans' sense is a namespace *inside* a context.
- **There are still no domain events.** `HoldingRemoved` was the only one ever planned and existed solely to clear a Redis cooldown across the Portfolio/Alerts line. A cooldown has a TTL and expires by itself, so the fix was deleting the event, not the boundary.

**Prices: two questions, two paths.** The dashboard asks *what is this worth now* and gets it by calling the provider directly on load — there is **no read-through, no fetch coalescer and no in-memory tier**. Alert evaluation asks *how has it moved over N minutes*, which needs history, which is the only reason a poller and a Redis window exist. The poller polls only tickers with an active alert; with none configured, nothing polls and the dashboard is unaffected.

- Two Redis price structures, deliberately not collapsed: `marketdata:last:{ticker}` is one value per ticker, never trimmed, written by any path that fetches, and is the dashboard's only fallback when the provider is down. `marketdata:prices:{ticker}` is the trimmed alert window. Different lifetimes — merging them would couple the dashboard's degradation to the alert retention setting.
- The poller and the window are **Phase 4**, not Phase 3. `minReplicas` goes to 1 there, for the same reason.
- The live deployment needs a real `FINNHUB_API_KEY` secret. `FakeQuoteProvider` is for the clean-clone path and the tests; leaving it on in Azure serves invented prices for real tickers.

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
- `Shared.Kernel` must stay framework-free — `Money`, `InvalidInput` and the CQRS interfaces, nothing else. There is no `AggregateRoot` and **no domain-event infrastructure**: `IDomainEvent`, `IDomainEventHandler` and `IDomainEventPublisher` are deleted. Phase 1 wrote `IDomainEvent`, found nothing raised it, and removed it; Phase 2 planned to bring it back for `HoldingRemoved`, which existed only because Alerts was a separate module. With Alerts inside Portfolio there is again no raiser, so reintroducing it would have repeated Phase 1's mistake. Anything taking an `IEndpointRouteBuilder` goes in `Shared.Api`.
- A module references only other modules' `.Contracts`. The compiler no longer enforces this now that Domain is public, so `Architecture.Tests` is the enforcement and is load-bearing — do not weaken or skip it.
- `.Contracts` holds records of primitives only. No EF reference, no aggregates, no strongly-typed IDs — use raw `Guid`. A strongly-typed id stays in the `.Domain` of the module that owns it: `UserId` lives beside `User` in `Identity.Domain`, and a module referencing a user it does not own stores a plain `Guid`. `Shared.Kernel` is for types that belong to **no** module — `Money`, `InvalidInput`, the CQRS interfaces — so moving `UserId` there would make the kernel the shared domain, which is what modules exist to prevent.
- Dependency edges: **Portfolio → MarketData** (dashboard prices), **Alerts → MarketData** (price windows), **Alerts → Portfolio** (`IUserHoldsTicker`, validation only). Nothing depends on Alerts. Identity sits off to the side with zero inbound runtime coupling; the JWT is self-contained. Keep it that way — it's the extraction-order argument.
- MarketData depends on nothing. It declares `ITickersToPoll` and the host supplies an adapter over `Alerts.Contracts`. Do not make MarketData read another module directly.
- One `DbContext` and one Postgres schema per module **that persists anything**, each connecting as its own role. The qualifier is not hedging — **MarketData is the stated exception and has no `DbContext`, no migration and no `MigratedModules.cs` entry.** Everything Phase 3 persists is one Redis key per ticker; an empty context to satisfy the shape would buy a zero-table migration, a `marketdata.__EFMigrationsHistory` row and a red `MigrationTests` assertion, for no behaviour. The `marketdata` schema and `marketdata_svc` role still exist and are inert — Phase 5's BYOK table is what makes them real. `alert_settings` and `fired_alerts` belong to the `alerts` schema and `AlertsDbContext`; `alert_settings` is keyed on user **and ticker**, so a threshold belongs to a position rather than to an account.

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

**Tests.** **416 passing and 2 skipped of 418 discovered**, at the end of Phase 3, measured from one `dotnet test` run with Docker up: unit (touch no infrastructure), architecture (reflection over assembly references), integration (Testcontainers Postgres + Redis, one collection fixture for the assembly, needs `public partial class Program;`). Use `FakeTimeProvider` for anything timer-driven.

| Assembly | Passed | Skipped |
|---|---|---|
| `Shared.Kernel.UnitTests` | 21 | 0 |
| `Modules.Identity.UnitTests` | 98 | 0 |
| `Modules.Portfolio.UnitTests` | 90 | 0 |
| `Modules.MarketData.UnitTests` | 61 | 0 |
| `Architecture.Tests` | 46 | **2** |
| `Api.IntegrationTests` | 100 | 0 |

Every skip is in `Architecture.Tests`, which is what makes the pinned number below checkable. The SPA is **not** in this total and is counted separately by `npm --prefix src/Web test` — **26 passing across 6 files**. Counts fell with the Alerts merge, rose with Portfolio, and rose again with Phase 3: a sixth test assembly, MarketData's own unit tests, Portfolio's dashboard calculator tests, and the dashboard integration suite.

**The 2 skips are architecture rules waiting on an empty assembly, and that number is pinned.** A rule that skips asserts nothing. Both remaining skips are `Identity.Contracts` — rule 1 (`Assembly_ReferencingAnotherModule_ReachesOnlyItsContracts`) and rule 2 (`ContractsAssembly_ReferencesNoPersistence`) — and both are correct: Identity is deliberately reached by nobody, so its `.Contracts` is deliberately empty. Rules 3, 4 and 5 no longer skip at all. `EmptyShells_AreExactlyThePhasesNotYetBuilt` fixes the exact list of shell assemblies, so a shell appearing or disappearing is a deliberate edit rather than a silent shift in what is enforced. Quoting a passing count without the skip count hides this.

⚠️ **Both numbers in the warning that used to sit here have changed, so the warning is rewritten rather than preserved.** It said the shell list was 6 while the skip count was 11, and that a reader could check 11 against this file and wrongly conclude the pinned list was intact. Phase 3 populated all five MarketData assemblies one task at a time, so **the shell list is now exactly one entry** — `StockPortfolio.Modules.Identity.Contracts`, re-derived from `EmptyShells_AreExactlyThePhasesNotYetBuilt` in `tests/StockPortfolio.Architecture.Tests/ModuleBoundaryTests.cs` — and the skip count is **2**, because that one shell skips two rules. The general lesson survives the specific numbers: **the shell count and the skip count are different quantities and any agreement between them is a coincidence.** Re-derive both from the test source; never copy either from here.

**A test that cannot fail is worse than no test**, because it reads as enforcement. Every architecture rule was verified by deliberately breaking it and watching it go red — that is how `PresentationAssemblies => AssembliesFor("Infrastructure")` was found, a copy-paste that pointed one rule at the wrong layer while reporting green. `ReferenceWalker_FindsEdgesThatDoExist` guards the same class of bug permanently: rules that pass by finding nothing need a companion that fails if the search finds nothing.

⚠️ **The subtler form: a test that can fail, but not on the difference it exists to detect.** `Dashboard_ProviderReturns429_Returns200NotError` failed one of three symbols and asserted the two served ones came back with their prices. It passed — and it passed *identically* under the implementation Phase 3 rejected, the single `try { provider } catch { redis-for-everything }` that throws away seventeen good prices because three failed. The reason is that the good prices had already been written to `marketdata:last:*` earlier in the same request, so the wrong implementation re-read the numbers it had just stored: same symbols, same amounts, same count. The only observable difference was the `IsLastKnown` flag, and the test did not look at it. It now asserts `IsLastKnown == false` on the served symbols. **Ask not "can this test go red?" but "can it go red on the specific mistake it is named after?"** — a fallback path that reproduces the happy path's numbers is invisible to any assertion on numbers alone.

## Where Identity is not a safe template

Identity was the only built module for a phase and a half, so it is what gets copied. It has no background
service, no outbound HTTP, no SSE, no value object and no cross-module dependency — five of its answers are
wrong elsewhere, and each fails silently. Decide these before writing the module, not after. Two of the five
rows below have now been *answered* by a shipped module rather than merely predicted; they are kept, marked,
because the question is what recurs, not the answer.

| Identity does | Wrong for | Decide instead |
|---|---|---|
| Repositories self-commit | **Nothing today.** This row used to warn that Phase 2's dispatch-before-save needed handler writes in one transaction. That argument is void twice over: §2.7 moved dispatch to after-save, then the Alerts merge deleted domain events entirely, so Portfolio's repositories self-commit exactly like Identity's | State the commit point on the repository interface's doc comment before the first handler exists — the question outlives the answer |
| Validates all config eagerly in `Add<M>Module` | **Answered, Phase 3.** A missing `Finnhub__ApiKey` is a *supported* state, so `FinnhubOptions.FromConfiguration` does not throw and `AddMarketDataModule` branches to `FakeQuoteProvider` instead. Eager validation there would have taken down `docker compose up`, which is the P0 gate | Validate eagerly only what the module genuinely cannot run without |
| Value converters are for ids | Phase 2 `Ticker`, Phase 4 `AlertDirection` (now also `Portfolio.Domain`) — Identity has no non-id value object, so "id" and "converter-backed type" happen to name the same set | A converter for every custom mapped type; register both `Properties<T>()` and `DefaultTypeMapping<T>()` |
| Canonical form is a `public static` on the entity (`User.NormaliseEmail`) | Only because email is a bare `string` with nowhere else to live — `Ticker` is a value object and canonicalises in its own factory | Canonicalise in the value object where there is one; a static on the entity only for bare primitives |
| Two host wire-ups finish a module | **Answered, Phase 3 — and the count was not the interesting part.** MarketData's own three (`AddMarketDataModule`, `AddMarketDataApi`, `MapMarketDataEndpoints`) are ordinary; the wire-up that bites is the **prerequisite it does not declare**. It injects `IConnectionMultiplexer`, which `AddStockPortfolioRedis` registers, and nothing in the module says so — delete or reorder that one line in `Program.cs` and the dashboard fails on the first request rather than at boot. Phase 4 adds SSE, a poller and a Redis subscriber | Count the wire-ups the module actually needs, **and the host services it silently assumes** — `Add` + `Map` does not mean wired |

## Traps

Each of these costs a day if you meet it cold.

- **`HasDefaultSchema` does not move `__EFMigrationsHistory`** (efcore#24127, closed *not planned*). Every context needs `MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")` or all three share one table and corrupt each other's bookkeeping. Never put `SearchPath=` in a connection string.
- **A constructor whose parameter names match mapped properties gets hijacked by EF for materialisation.** Binding is by convention and cannot be configured. This is *fine and intended* here — the single private all-args constructor only assigns. It becomes a trap the moment a guard is added inside it, because EF then re-runs that guard on every row of every `SELECT`. Guards belong in the static factory.
- **EF binds a complex type's *own* constructor for materialisation, exactly as it does an entity's.** The guard-free-constructor rule above is therefore about **value objects too, not just entities** — and `Money` is already in breach: `Money(decimal, string)` calls `currency.ToUpperInvariant()`, so that allocation runs on every row of every `SELECT`, confirmed from a materialiser stack trace. Accepted for Phase 2 (three columns, one row per position) and recorded here as a known cost, not as settled good practice.
- **Projecting a `ComplexProperty` translates fine — project its members anyway, for a different reason.** EF Core documents complex types as projectable, and they translate to their constituent columns; the one documented restriction is projecting a complex type through an *optional navigation*, which the dashboard read does not do. So `.Select(h => new HoldingRow(…, h.AveragePrice, …))` would have worked. `HoldingQueries.GetVisibleHoldingsAsync` projects `h.AveragePrice.Amount` and `.Currency` and rebuilds `Money` afterwards **to keep `Money`'s constructor off the per-row materialisation path** — the `ToUpperInvariant()` breach in the entry above. Phase 3's plan asserted the projection "will not translate"; that reason was wrong and the instruction it produced was right, which is the combination most likely to be copied somewhere it does not hold.
- **A `ComplexProperty` cannot be a constructor parameter** ([efcore#31621](https://github.com/dotnet/efcore/issues/31621), open — milestoned `11.0.0` in Feb 2026, then pushed back to `Backlog` with a `blocked` label in Jun 2026, so it is not arriving). Phase 2's `Holding` maps `Money AveragePrice`, and `private Holding(…, Money averagePrice, …)` fails model building. **The failure is at model build, i.e. host startup — not at first query**, so it takes the whole process down rather than one request. This does **not** require a parameterless constructor — EF documents that *"not all properties need to have constructor parameters"* and sets the rest after construction. Omit only the complex member; the factory assigns it afterwards, since `private set` is reachable from inside the type.
- **A complex type's members are not mapped by convention if they have no setter**, and `Money.Amount`/`Currency` are get-only. A bare `builder.ComplexProperty(h => h.AveragePrice)` therefore maps nothing, cannot bind `Money`'s constructor, and throws *"No suitable constructor was found for the type 'Holding.AveragePrice#Money'"* at model build. Map each member explicitly inside the lambda — which is required anyway, because Npgsql does not snake-case `avg_price_amount` for you. The cost: **a `Money` member added later is silently unmapped** until someone adds a `.Property()` line, so a member-count assertion guards it.
- **`PropertyAccessMode.PreferField` is the default**, so EF writes the backing field and never calls your setter. Validation in a setter silently never runs — which is moot now that entities have no settable surface, but it is why they don't.
- **`Maximum Pool Size=2` on every connection string.** Azure Postgres B1ms allows 35 user connections and a different username is a different Npgsql pool. **Count what opens a pool, not what is defined** — this arithmetic was published four different ways across four files and every one was wrong, in both directions. `db/init/01-roles.sql` creates five roles (`migrator` plus four `*_svc`) and four schemas, but `Program.cs` registers exactly **two** `DbContext`s, Identity's and Portfolio's, so **two Npgsql pools exist per replica**. At `Maximum Pool Size=2` and `maxReplicas: 2` that is 2 × 2 × 2 = **8**, and `migrator` runs as a separate job rather than concurrently with the API. The default of 100 would make it 400. PgBouncer is unavailable on Burstable. MarketData has no `DbContext` and opens no pool; `alerts_svc` is created and nothing connects as it — see `docs/deferred-work.md` E1.
- **An unhandled exception in a `BackgroundService` kills the host** (`StopHost` is the default). The poll loop needs an in-loop `try/catch`.
- **Assigning a `DelayGenerator` silently disables `Retry-After` handling**, which is honoured by default. `ShouldRetryAfterHeader` is not a flag — its setter *is* the `DelayGenerator` assignment, and `MaxDelay` is ignored for generated delays. Note that Retry-After being honoured is a Polly fact, not a Finnhub fact: there is no evidence Finnhub emits the header, so the client-side token bucket is what actually carries the limit.
- **`AddStandardResilienceHandler`'s defaults are decorative and its validator is startup-fatal.** `CircuitBreaker.MinimumThroughput` ships at 100, so the breaker can never open for a twenty-ticker dashboard — it is configured down to 10. And `HttpStandardResilienceOptionsCustomValidator` registers with `AddOptionsWithValidateOnStart`, so `AttemptTimeout > TotalRequestTimeout`, or `SamplingDuration < 2 × AttemptTimeout`, takes the host down at boot — i.e. takes down `docker compose up`, the P0 gate. The shipped values satisfy both (5 s < 15 s; 30 s ≥ 10 s).
- **Finnhub's `/quote` cannot tell a non-existent symbol from a healthy one it blipped on.** Both come back as `c: 0` — present, zero — so an existence check written as "`/quote` returned a non-null, non-zero `c`" answers *unknown* to every transient failure and would permanently reject a valid holding after one bad second. The check is `/search?q=` with an **exact case-insensitive match on `result[].symbol`**, never `count > 0`, because `/search` is fuzzy and `q=AAP` returns AAPL. A null search result means the provider could not answer and the check **fails open** — a Finnhub outage must not reject a purchase. Phase 3's spec asserted the opposite mapping (all-zero ⇒ `UnknownTicker`); it was reversed, and the only primary evidence — [Finnhub-API#54](https://github.com/finnhubio/Finnhub-API/issues/54), intermittent all-zero responses for AAPL/TSLA/FB — points the other way. Entitlement failures surface as **401 or 403**, both with the same body, and neither is ever retried.
- **Never add `UseResponseCompression()`** — it buffers `text/event-stream` and the alert feed dies silently.
- **ACA `requestIdleTimeout` is 4 minutes and 4 is the floor** on Consumption. The SSE heartbeat must fire every 20s. `SseFormatter` has no comment API, so use a named `ping` event.
- **ACA liveness must not check Postgres or Redis.** A dependency blip then becomes a container restart loop, turning a degraded app into a down one.
- **TanStack Query v5.89.0 renamed the mutation callbacks' `TContext` generic to `TOnMutateResult` and *appended* a new `context` (`{ client, meta, mutationKey }`) as the last parameter of each**; `mutationFn` gained a second argument. **Positions did not move** — the `onMutate` snapshot is still argument 3 in `onError`/`onSuccess` and 4 in `onSettled` — so pre-5.89 rollback code still compiles and still restores the correct value. Pinned version is 5.101.4. This entry previously claimed the opposite (*"every optimistic-update tutorial written before Sept 2025 rolls back the wrong snapshot"*). That was **false**, disproved three ways against the installed 5.101.4 — the shipped `.d.ts`, the compiled `mutation.js` call sites, and a compile test with a negative control. It is corrected rather than deleted because leaving it was not neutral: it would have driven a pointless rewrite of working code.
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
- **Windows Application Control can block a freshly built DLL** with `0x800711C7 — An Application Control policy has blocked this file`. It surfaces as a `FileLoadException` in **`Architecture.Tests`**, because that is the suite that reflects over every assembly — but the blocked assembly is the one **named in the exception message**, not the test project, and only the handful of cases that touch it go red (four of them, over `MarketData.Api`, the last time). Not a code fault. Delete the **named** project's `artifacts/bin` and `artifacts/obj`, then rebuild — deleting is the point, since an incremental rebuild alone leaves a DLL whose inputs have not changed exactly where it is.
- **`Microsoft.OpenApi` must stay on 2.x.** 2.0.0 carries GHSA-v5pm-xwqc-g5wc so pin ≥2.11.0, but 3.x makes `IOpenApiMediaType.Example` read-only while the ASP.NET Core OpenAPI source generator still assigns to it (`CS0200`).
- **A new pay-as-you-go subscription has almost no resource providers registered**, and the obvious pre-flight check does not catch it: `az provider show -n Microsoft.App --query "…locations"` happily returns a region list for a provider the subscription may not use. Availability and entitlement are different questions. The deploy dies at the first resource with `MissingSubscriptionRegistration`. Seven needed registering here — `App`, `Cache`, `ContainerRegistry`, `DBforPostgreSQL`, `ManagedIdentity`, `OperationalInsights` (only `Authorization` was pre-registered).
- **`appLogsConfiguration: { destination: 'none' }` is rejected at preflight** with *"App Logs destination 'none' not supported. Supported values: 'log-analytics', 'azure-monitor' or none"* — a message that lists as valid the exact value it just refused. The trailing "or none" means **the property omitted**, not the string. Leave the block out entirely.
- **An `existing` resource plus `listKeys()` creates no dependency on the module that builds it.** `main.bicep` declared the Redis cluster as `existing` and read its key, with a comment asserting the ordering was safe because the container app referenced the module's `hostName`. ARM hoisted the key lookup anyway and a first deploy into an empty group failed with `ParentResourceNotFound` — then "passed" on retry, because by then the cluster existed. Build the connection string **inside** the module where the resource is really being created, and return it as a `@secure()` output.
- **GitHub now issues immutable OIDC subject claims.** The federated credential subject is `repo:<owner>@<ownerId>/<repo>@<repoId>:ref:refs/heads/main`, not the `repo:<owner>/<repo>:…` form every guide (and every pre-2025 workflow comment) still documents. The old form matches nothing and fails with `AADSTS700213`. Read the subject out of the failing run's log and register that exact string; registering both forms is harmless.

## Deployment

📄 **[docs/DEPLOYING.md](docs/DEPLOYING.md) is the runbook; [the design record](docs/superpowers/specs/2026-08-02-azure-deployment-design.md) is the why. Read the runbook before any deploy work.** This section is the summary; those files have the procedure, the cost model, the four decisions, the six-step verification and the six failed attempts. Everything below is derived from them.

Three targets: `docker compose` (whole stack, local, the P0 gate), **GitHub Pages** (SPA, static, `VITE_API_BASE_URL` baked in at build), **Azure Container Apps** (API only).

**A deploy does not need `az` on your machine.** `deploy.yml` fires on **push to `main`** or `workflow_dispatch`, and installs Bicep and runs `az deployment group what-if` inside the runner. `az` locally only buys you a pre-flight `bicep build` and a local `what-if`. So "no Azure CLI here" blocks the rehearsal, not the deploy — do not record a deploy as blocked on that alone.

Deploying Phase 3 therefore needs exactly two things: this branch merged to `main`, and `FINNHUB_API_KEY` set as a repository secret. It is optional and defaults to empty, and empty means the public URL serves invented prices for real tickers — which reads as broken rather than as a thoughtful fallback.

⚠️ **`teardown.yml` deletes the whole resource group once the `deleteAfter` tag passes**, and a redeploy re-stamps it to today + 14 days. As of 2026-08-05 the tag reads **2026-08-16**. Deploying extends the window by using it; not deploying lets it expire.

`main.bicep` currently passes **`minReplicas: 0`**, against the module default of 1, purely to cut cost — the subscription is personal and pay-as-you-go. That is only safe while nothing needs an always-on replica, which is still true after Phase 3: there is no `BackgroundService`, `IHostedService` or `PeriodicTimer` anywhere in `src/`.

**This line used to say "Phase 3 must put it back to 1", and that was stale before Phase 3 started.** The quote poller moved from Phase 3 to Phase 4 when the dashboard was unpicked from the alert infrastructure — the dashboard fetches on demand and needs nothing running in the background — so **Phase 4** is where `minReplicas` goes to 1, alongside the poller. The exit checkbox "no `BackgroundService`, `PeriodicTimer` or `IHostedService` anywhere in `src/`" is what keeps the two in step: while it holds, 0 is correct.

The honest cost of 0 is cold start: from zero, the first dashboard request pays container start **and then** the N-call fan-out, serially. `refetchInterval` and `refetchOnWindowFocus` keep the app warm for the rest of a session, so it is a first-load cost rather than a per-request one.

Cost is bounded by **time, not by budget**. Pay-as-you-go has no Azure spending limit, and a budget only emails — it cannot stop anything. `deploy.yml` stamps a `deleteAfter` tag on the resource group and `teardown.yml` deletes the group once that date passes. **Live deployment, healthy as of 2026-08-05**: resource group `stockportfolio-rg` in `polandcentral`, ~$1.26/day, API at `stockp-api-qdgz3wugqbihs.icysea-481b5825.polandcentral.azurecontainerapps.io`, SPA at `dilicidum.github.io/StockPortfolio`. It serves **pre-Phase-3 code** until this branch reaches `main`. Postgres Flexible B1ms and Azure Managed Redis Balanced B0 with HA off — **not** Azure Cache for Redis, which is retiring.

Cross-origin is permanent, so the SSE endpoint uses a single-use 30-second ticket rather than a header. GitHub Pages needs `404.html` copied from `index.html` plus a Vite `base` and matching router `basepath`.

## Deliberately not built

These were considered and cut. Don't reintroduce them without asking.

- **Alert replay** — no cursor, no `Last-Event-ID`, no 24h backfill. Req 9 asks for an event on breach, a background check, and a simulate button. History is a plain `GET`; the stream hook invalidates the query on reconnect.
- **Watchlist** — «перелік акцій» in req 8 sits inside *dashboard settings*, so it means which of your holdings show on the dashboard. That's `is_visible` on `holdings`.
- **A cached ticker table in MarketData** — the poll list is read live each cycle, from Alerts. Removing it also removed two event handlers, a reconciliation pass and a divergence failure mode.
- **Raw SQL** — see Conventions.
- **Trading-hours gating** — dropped entirely. It existed to stop pointless polling outside market hours; the poller now only runs for tickers with an active alert, and the dashboard fetches on demand, so there is nothing to gate.
- **WebSockets and SignalR** — SSE is the transport. The README carries the decision matrix; the UI badge says "Live (SSE)", never "WS Live".
