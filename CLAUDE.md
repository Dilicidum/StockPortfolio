# StockPortfolio

Stock-portfolio tracker: live quotes, P&L, threshold alerts pushed over SignalR. .NET 10 modular monolith + React SPA.

Built against a take-home brief (`TZ_Stock_Portfolio_App.docx`, Ukrainian). **P0 completion is the acceptance gate** — auth, quotes via TanStack Query, TanStack Router with 4+ routes, portfolio CRUD, dashboard with totals and P&L, parameterised DB access, and `docker compose up` bringing the whole stack up in one command. P1 and P2 add points; P0 failing means nothing else counts.

## Current state

**All six phases are functionally complete.** `dotnet build` is clean at 0 warnings and `docker compose up`
brings the whole stack up from a clean volume. Phase 5 shipped the settings surface: a theme applied before
the first paint, English and Ukrainian, a saved dashboard refresh interval, a visibility toggle per position,
and a per-user market-data API key encrypted at rest. **Phase 6 is built and not yet verified in a browser
or deployed**, which is what a phase being *done* requires — so its implementation plan stays.

**Phase 5 closed a P0 gap, not just extras.** The brief lists the minimum routes as login, dashboard,
portfolio and **settings**, and the settings route did not exist until this phase.

**Phase 6 is graceful failure, and three of its answers are counter-intuitive.** Stopping Redis changes
nothing on the dashboard — the provider is asked directly, so the cache was never on that read path — and it
suppresses alerts, because a stale price is a degraded read while a made-up price history is a wrong alert. A
rejected provider key is reported as unhealthy with its reason and the app keeps serving last-known prices;
it does **not** fall back to the fake provider, which would serve invented numbers for real tickers. And a
last-known price is shown only while the market has been open an hour or less since it was recorded, counting
open minutes rather than wall-clock ones, so Friday's close stands all weekend and overnight and a dash means
a real trading hour has passed with nothing to show. Holidays are not handled.

Two things were deleted rather than built. The **client-side token bucket** was sized to one provider's free
tier, and the brief says free-tier limits are not a problem, so it went — what holds the rate now is retry
honouring `Retry-After`, the circuit breaker, per-ticker isolation and the last-known-price fallback. And the
**anticorruption layer's enumerated catches** were replaced with a broad catch that lets only cancellation
through: three named exception types meant a 200 carrying an HTML error page from a WAF or CDN escaped and
turned the dashboard into a 500, which is the exact failure the error-handling requirement exists to prevent.

**MarketData now persists.** It gained its first `DbContext` and its first two tables, so there are four
migrated contexts and four schemas with migration history. Encryption is a port the module declares
(`ISecretProtector`, `IKeyRingStore`) and the host implements, because `.Infrastructure` may not reach
ASP.NET Core — the Data Protection packages both trip that rule transitively.

Still open:

- `TokenPolicy` values (token lifetimes, rotation, grace window) are provisional and unsigned-off.
- Server-generated text (API validation messages, a fired alert's reason) stays English; the backend does no
  language negotiation.
- Nothing since Phase 4 has been deployed, so Phases 5 and 6 are unproven against the public URL.

[docs/deferred-work.md](docs/deferred-work.md) is the register for anything deferred, unbuilt or rejected. Something described in a plan and missing from the code belongs there too, not only defects found in code that exists. Read it before assuming an unimplemented feature is simply "not that phase yet".

## Plans

Three levels, each with one job.

1. **The overall plan** — [docs/plan/00-overview.md](docs/plan/00-overview.md). The whole system: the core ideas, the decisions, how it behaves, why it behaves that way, and the flow from one end to the other.
2. **One plan per phase** — the decisions for that phase, in more detail.
3. **One implementation plan per phase** — the tasks and the order to build them in. It is written when the phase starts and **deleted when the phase ships**. It is the only place where file names and type names belong.

A plan holds ideas, decisions, reasoning, behaviour and architecture. It never holds class names, method names, file paths, line numbers, task numbers or test names. Domain words and public API routes are fine.

Phases 1 to 5 have shipped, so none of them has an implementation plan any more. **Phase 6 is in flight**, so
`phase-6-implementation.md` exists and carries its build order, its file names and its deployment and
browser-verification tasks. Delete it on the day those pass, not before — a phase is done when it runs in a
browser *and is deployed*, not when its tests pass.

A plan is short enough to read from start to finish in one sitting. When something changes, change it everywhere and leave only the new version. Do not write down what it used to say — git keeps that.

**`docs/plan/` holds plans and nothing else** — the overview and one file per phase, so seven at rest, plus one implementation plan while a phase is in flight. **It is at eight today**, the eighth being Phase 6's. The plan outlives the last commit of code by however long verification takes.

## Reference documents

These describe the shape of the system rather than the order it gets built in, so they live in `docs/reference/`, not in the plans folder.

- [docs/reference/er-diagram.md](docs/reference/er-diagram.md) — the data model: tables, what lives in Redis instead, which indexes carry weight, and which tables exist today.
- [docs/reference/module-interactions.md](docs/reference/module-interactions.md) — which module depends on which, what crosses each line and why.
- [docs/reference/service-interactions.md](docs/reference/service-interactions.md) — the runtime picture: browser, the one process, and which module talks to Postgres, Redis or the price provider.
- [docs/reference/module-boundaries.md](docs/reference/module-boundaries.md) — why the boundaries are where they are, and the three places one was deliberately not drawn.
- [docs/reference/bounded-contexts.md](docs/reference/bounded-contexts.md) — what kind of relationship crosses each boundary.
- [docs/reference/identity-contracts.md](docs/reference/identity-contracts.md) — how sessions and tokens behave, and what about them is fixed.

Where `bounded-contexts.md` and `module-boundaries.md` disagree, `module-boundaries.md` decides *where* a boundary goes and `bounded-contexts.md` decides *what kind* of relationship crosses it.

Read before touching code: the overview, then the phase file you are working in. [docs/Initial.md](docs/Initial.md) is the original architecture essay and is historical; where it conflicts with a plan, the plan wins.

Work phase by phase. A phase is done when it runs in a browser, not when tests pass.

## Operational documents

Read these before touching deploys, Bicep, workflows, cost or teardown.

- [docs/DEPLOYING.md](docs/DEPLOYING.md) — **the runbook. Start here.** How to deploy (push to `main`, and nothing else — never run `az deployment group create` by hand), what exists, how to verify, the cost ceiling, and five failures that each cost a deploy cycle.
- [docs/superpowers/specs/2026-08-02-azure-deployment-design.md](docs/superpowers/specs/2026-08-02-azure-deployment-design.md) — the **why**: the cost model, the four decisions behind `minReplicas: 0` and the time-bounded ceiling, the six-step verification, and the six failed attempts. It sits outside `docs/plan/` because `docs/plan/` is the numbered product build and this cuts across it.

Four of their failure cases are copied into Traps below; the rest are only there.

## Commands

```bash
docker compose up                    # whole stack, from a clean clone, no API key needed
dotnet build
dotnet test                          # the integration suite needs Docker running; counts are in Tests below
npm --prefix src/Web run dev
npm --prefix src/Web test

# migrations: --project is the module's Infrastructure, --startup-project is always the host
dotnet ef migrations add <Name> --context <Module>DbContext --output-dir Persistence/Migrations \
  --project src/Modules/<M>/StockPortfolio.Modules.<M>.Infrastructure --startup-project src/Host

az deployment group what-if -g <rg> -f infra/main.bicep    # before any deploy
```

`/openapi/v1.json` is served in Development only — which `docker-compose.override.yml` makes the default, so it *is* reachable at `:8080` on a plain `docker compose up`, as well as from `dotnet run --project src/Host`.

With no `Finnhub__ApiKey` configured the app uses `FakeQuoteProvider` and logs a warning. That is deliberate — Finnhub killed its sandbox in 2022, so the demo must work without a key.

## Architecture

Four modules — `Identity`, `Portfolio`, `MarketData`, `Alerts` — each with **five** projects: `.Contracts` / `.Domain` / `.Application` / `.Infrastructure` / `.Api`. **All four exist.** Plus `Shared.Kernel`, `Shared.Api`, the `Host` project and a `Migrator` console. Assembly and namespace prefix is `StockPortfolio.`; modules are `StockPortfolio.Modules.<Module>.<Layer>`.

**Boundaries are argued from the cost of pulling a module out, not from subdomain labels.** The test for every boundary: would it survive becoming a network call? Four questions — does anything need a transaction across it, is the number of calls bounded, can one side fail while the other keeps working, is there exactly one writer per table. Full reasoning in [docs/reference/module-boundaries.md](docs/reference/module-boundaries.md).

- **Alerts is its own module.** Sharing a word does not make two models one: `Ticker` means the same thing in Portfolio and in Alerts, and they are still two contexts. Different words are enough to prove two contexts exist, but they are not required.
- `AlertSettings` and `FiredAlert` never share a transaction with `Holding`, no rule spans any two of the three aggregates, they are written at different times, and alerts can be down while the dashboard still renders.
- **Core / supporting / generic subdomain labels are not used.** They are real DDD, they changed no code here, and they mix up three different things: a subdomain is the problem, a bounded context is a model boundary, and a module in Evans' sense is a namespace *inside* a context.
- **There are no domain events.** The only one ever planned was `HoldingRemoved`, and it existed only to clear a Redis cooldown across the Portfolio/Alerts line. A cooldown has a TTL and clears itself, so the answer was to delete the event, not to move the boundary.

**Prices: two questions, two paths.** The dashboard asks *what is this worth now* and answers it by calling the provider directly on load — there is **no read-through cache, no request coalescing and no in-memory tier**. Alert evaluation asks *how has it moved over N minutes*, which needs history, which is the only reason a poller and a Redis window exist. The poller polls only tickers with an active alert; with no alerts configured nothing polls, and the dashboard is unaffected.

- Two Redis price structures, deliberately kept apart: `marketdata:last:{ticker}` is one value per ticker, never trimmed, written by any path that fetches, and is the dashboard's only fallback when the provider is down. `marketdata:prices:{ticker}` is the trimmed alert window. They have different lifetimes — merging them would tie the dashboard's fallback to the alert retention setting.
- **`marketdata:name:{ticker}` is a third key and is not a price at all.** It holds the company name, written whenever a ticker search sees one, with a seven-day expiry. A cached price would be wrong within a second; a cached name is right for years, and the expiry only exists so a company that renames itself corrects without anyone acting. Search *results* are not cached — the term is arbitrary and rarely repeated, while ticker-to-name is a small set read on every page. Names never reach the provider on a page render: the holdings page has no provider dependency and a cosmetic field must not give it one.
- The poller and the window are **built**, and `minReplicas` is **1** because of them. The poller reads its target list at the start of every cycle; with no enabled threshold anywhere it acquires its leases, gets an empty list and does nothing, so the provider is never called.
- The live deployment needs a real `FINNHUB_API_KEY`. `FakeQuoteProvider` is for the clean-clone path and the tests; leaving it on in Azure serves invented prices for real tickers.

**Accessibility follows the onion, not a blanket `internal`.** `internal` is per-assembly and a module is five assemblies, so "everything internal outside `.Contracts`" cannot compile — `Identity.Infrastructure` could not see `User` in `Identity.Domain`.

| Layer | Holds | Accessibility |
|---|---|---|
| `.Contracts` | records of primitives, for other modules | `public` |
| `.Domain` | entities, rules | `public`, own module only |
| `.Application` | commands, results, handlers, abstractions | `public` |
| `.Infrastructure` | DbContext, repositories, hashing, tokens | **`internal`** except `<Module>Module` |
| `.Api` | endpoints, request records, validators | `public` (leaf project) |

Two reference rules are enforced by the compiler and checked again by `Architecture.Tests`: **`.Infrastructure` never references ASP.NET Core; `.Api` never references EF Core or its own `.Infrastructure`.** They meet only through `.Application/Abstractions`.

- Inbound HTTP is presentation, not infrastructure. Do not move endpoints back into `.Infrastructure` (tried, wrong) or up into the **host** (that makes the host the place every feature has to touch). `StockPortfolio.Host` in `src/Host` is the composition root and defines no business routes of its own; `StockPortfolio.Modules.<M>.Api` is a module's HTTP layer — different assemblies, and now different words.
- `Shared.Kernel` must stay free of frameworks — `Money`, `InvalidInput` and the CQRS interfaces, nothing else. There is no `AggregateRoot` and no domain-event machinery: `IDomainEvent`, `IDomainEventHandler` and `IDomainEventPublisher` do not exist. Nothing raises an event, so nothing needs them. Anything taking an `IEndpointRouteBuilder` goes in `Shared.Api`.
- A module references only other modules' `.Contracts`. The compiler cannot check this now that Domain is public, so `Architecture.Tests` is the only thing enforcing it — do not weaken or skip those tests.
- `.Contracts` holds records of primitives only. No EF reference, no aggregates, no strongly-typed IDs — use raw `Guid`. A strongly-typed id stays in the `.Domain` of the module that owns it: `UserId` lives beside `User` in `Identity.Domain`, and a module referencing a user it does not own stores a plain `Guid`. `Shared.Kernel` is for types that belong to **no** module — `Money`, `InvalidInput`, the CQRS interfaces — so moving `UserId` there would turn the kernel into a shared domain, which is exactly what modules exist to prevent.
- Dependency edges: **Portfolio → MarketData** (dashboard prices, and company names for both tables — two separate contracts on purpose, because a price outage and a missing name are different failures and must not share an interface), **Alerts → MarketData** (price windows), **Alerts → Portfolio** (`IUserHoldsTicker`, validation only). Nothing depends on Alerts. Identity sits off to the side with nothing depending on it at runtime; the JWT is self-contained. Keep it that way — it is the reason Identity would be the easiest module to pull out first.
- MarketData depends on nothing, and the poller did not change that. It declares **two** ports of its own — one inbound, *which tickers am I to sample*, and one outbound, *a fresh sample landed* — and the host adapts both to `Alerts.Contracts`. Both are worded as MarketData's own need rather than as "ask Alerts", so if either answer ever comes from somewhere else only the host adapter changes. Do not make MarketData read another module directly.
- One `DbContext` and one Postgres schema per module **that persists anything**, each connecting as its own role — Identity, Portfolio, Alerts and, since Phase 5, MarketData, so **four registered contexts**. MarketData was the stated exception through Phase 4 and stopped being one when per-user API keys arrived: `marketdata` now holds `user_provider_keys` and `data_protection_keys`, and the `marketdata_svc` role that sat unused since Phase 1 is finally real. `alert_settings` and `fired_alerts` belong to the `alerts` schema and `AlertsDbContext`; `alert_settings` is keyed on user **and ticker**, so a threshold belongs to a position rather than to an account.

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

**A handler returns `OneOf<…>` of its outcomes directly.** No `[GenerateOneOf]`, no named union class — `Task<OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>>` says in the signature what a wrapper would hide behind a name, and the DI registration and the endpoint's injected `ICommandHandler<,>` both spell out the same closed generic. `<UseCase>Result` is the **success payload record**, never the union. Failure cases are records beside the use case that returns them, not pooled in an `Errors.cs`; `InvalidInput` (in `Shared.Kernel`) is the one shared failure, because every layer can produce a field-plus-message.

Map to a result via `.Match`, and **inside a `.Match` arm write `Results.X(...)`, never `TypedResults.X(...)`.** `TypedResults.Ok` returns `Ok<T>` and `TypedResults.Created` returns `Created<T>`, so a two-arm `.Match` hands the compiler two unrelated types and it refuses to pick one — CS0411, which is why `.Match<IResult>` used to be spelled out at every call site. `Results.X(...)` returns plain `IResult`, so every arm agrees and no `.Match` needs a type argument. The helpers in `ProblemDetailsExtensions` return `IResult` for the same reason. Outside a `.Match` — a plain `return TypedResults.Ok(result);` at the end of a handler — `TypedResults` stays. Every case must be handled, and that is structural: `.Match` takes one delegate per case, so adding a case breaks every call site. Never silence CS8509 with `_ => throw`, and never `switch` over `.Value` — that is the only way to lose the guarantee. No suppressor package is needed or installed. **Name every `.Match` lambda parameter** — `emailTaken =>`, not `_ =>`. The discard costs the one word that says which case this is.

**Rich domain, and no base class.** There is no `AggregateRoot<TId>`; each entity declares its own `Id`. An entity has **exactly one constructor**: private, taking every mapped value, assigning and nothing else. No parameterless constructor, no object initialiser, no public setter — a half-built entity is not representable, and the static `Create(...)` returning a OneOf is the only way in.

The constructor must contain no validation checks. EF matches constructor parameters by name and *will* pick this one when it loads rows, so anything it does runs on every row of every `SELECT`. That is the real trap — not the constructor itself. Validation lives in the factory, which EF never calls.

**Use `OneOf.Types.Success` and `OneOf.Types.NotFound`.** They ship with the package. Do not redeclare them per module, and never put a `Success` type in an errors file.

**No unit of work, and no `ConfigureAwait(false)`.** `DbContext` already *is* a unit of work, so a second one over it only adds a name; a repository write method saves before it returns, and because every repository in a module shares one scoped `DbContext`, one save carries whatever else the handler changed. Where two writes must land together, order them so the last repository call is the one that saves. `ConfigureAwait(false)` is for general-purpose libraries — ASP.NET Core has no `SynchronizationContext`, so it is noise here.

**Every context retries a failed connection three times, two seconds apart, and that constrains any transaction added later.** `EnableRetryOnFailure` is on all four. Two consequences, and the second is a standing rule rather than a note. It makes EF buffer every result set, which is a real cost paid on every query. And **an explicit transaction must be opened inside the execution strategy, and the work inside it must be safe to run twice** — EF refuses a user-initiated transaction otherwise, at run time, with a message telling you to use `CreateExecutionStrategy`. There is no explicit transaction in the codebase today; the first one has to obey this. The numbers are deliberate rather than the defaults: six attempts backing off to thirty seconds would leave a request against a stopped Postgres hanging for about a minute, with the browser on a spinner and the readiness probe timing out long before the answer arrived.

**No defensive null checks where the compiler already answered.** `ArgumentNullException.ThrowIfNull(app)` in an endpoint-mapping extension, or `ThrowIfNull(command)` in a handler whose command the API just built, protects against a caller that cannot exist. Keep those checks on public entry points that take input from a user.

**An abstraction returns a named type and takes the module's own value objects.** `IPriceWindowStore.ReadAsync` is the one exception — it returns `IReadOnlyList<(DateTimeOffset At, decimal Price)>` and takes a `string` ticker, and it is the only tuple-returning abstraction in the codebase. It was built that way deliberately and is recorded here rather than left to look like an oversight somebody should tidy. A second one is a convention change, not a precedent already set.

**Validation has three layers, and only one uses result types.**

| Layer | Where | Mechanism |
|---|---|---|
| Shape — is this even an email? | FluentValidation on the **request**, `.Api` | generic `ValidationFilter<T>` : `IEndpointFilter` returns **400** |
| Context — does this user exist? allowed? | handler, `.Application` | OneOf result case |
| Rule — a User can never have a blank email | entity, `.Domain` | **throws** |

**"Is this address taken?" is a context question, so the handler asks it.** `RegisterUserCommandHandler` does a `FindByEmailAsync` and returns `EmailAlreadyUsed`; it does not insert and then read a unique-violation back out of the exception. The look-up goes through `User.NormaliseEmail` — the one place the canonical form is defined, used by `User.Create` on the way in and by both handlers on the way out, because a lookup that normalises differently from what was stored simply misses.

Known and accepted: two simultaneous registrations of one address can both pass the check, and the loser then hits the unique index and comes back as **500 rather than 409**. The index stays — it is what keeps the data correct — and the window is a millisecond wide. Add a `DbUpdateException` catch only if that 500 ever actually shows up.

Shape validation is an **endpoint filter, not a DI decorator**. A decorator would have to return an unconstrained `TResult` and cannot build a failure value; a filter sits in the HTTP pipeline and can `return TypedResults.ValidationProblem(...)` directly. Inject `IValidator<T>`, never `IEnumerable<IValidator<T>>` — the collection form silently validates nothing when a validator is missing. Validators do no I/O: "is this email taken?" is a context question and belongs in the handler as a result case. `LoggingDecorator` stays a decorator; it has no `TResult` problem. Do not use the built-in .NET 10 `AddValidation()` — it is driven by DataAnnotations attributes and is awkward for conditional or cross-field rules.

**Endpoint handlers return `Task<IResult>`.** Not `Task<Results<Created<T>, ProblemHttpResult, …>>` — the typed union repeats in the signature what `.Produces(...)` already declares, and it grows a generic argument every time a case is added. `.Match(...)` still forces every case to be handled, because that comes from the union's size, not from the return type.

The trade is worth knowing: the typed union made the compiler reject a result the signature had not declared. `.Produces(...)` metadata is now the **only** description of what a route emits, so it can fall out of step with the code and nothing will fail. That is why the next rule says to check against a live response.

**Every endpoint declares every status it can emit.** `.Produces<T>(200)` for the success shape, then `.ProducesValidationProblem()` (400), plus `.ProducesProblem(...)` for each of 401 / 409 / 415 / 500 that the route can actually reach. `ProducesValidationProblem()` is metadata only — it documents the 400 that `ValidationFilter<T>` returns and is shorthand for `Produces<HttpValidationProblemDetails>(400, "application/problem+json")`.

415 and 500 are declared as `problem+json` because `AddProblemDetails()` **and** `UseStatusCodePages()` are both registered, which is what gives framework-generated bare status codes a body. Drop either one and those declarations become lies. Make a real request before adding a status — a bare `Produces` claiming a body that never arrives is worse than an undeclared response. Read the result back from `/openapi/v1.json`, not from the source.

**A missing body is 400, not 415.** Only a *wrong* `Content-Type` produces 415, and the two are easy to conflate when writing the `.Produces` list from memory. `POST /api/holdings` had declared 415 since Phase 2 and no test in the repo ever drove it; the alert-settings route is the first place that declaration was demonstrated against a real request. A status nothing has provoked is a guess, however confidently it is written down.

**Comments: one line, and only where the code cannot say it.** No `<remarks>`, no `<param>`/`<returns>`/`<exception>` blocks, no banner rules. A doc comment is a single `/// <summary>…</summary>`. If a comment must span lines to make sense, the reasoning belongs in `docs/plan/` or a commit message, not in the file.

**`src/Web` carries no comments at all** — application code, tests, `vite.config.ts`, `nginx.conf`, the `Dockerfile` and `.env.example` alike. Every one was deleted deliberately: about 1,800 lines, a fifth of the folder. Of the 359 blocks under `src/`, 303 narrated the code, argued with a version that no longer existed, or restated an API contract the generated OpenAPI document already owns; 43 more repeated something this file already said. The 12 facts that were genuinely load-bearing became Traps entries below, which is why several of those entries are about the browser. **Do not reintroduce a comment there.** A fact worth writing down goes in Traps, where one place holds it and it cannot rot beside code that has moved on. This rule is the frontend only — C# keeps the one-line rule above.

**Three lines in `src/Web` look like comments and are not**: the `/// <reference types="…" />` at the top of `vite.config.ts` and `src/vite-env.d.ts`, and the `#!` on `scripts/check-locale-parity.mjs`. They are instructions to TypeScript and to the shell. Deleting the first one breaks `npm run typecheck` with an error about `test` not existing on `UserConfigExport`, two steps from the cause.

**Money is `decimal` server-side and serialised as strings.** Never compute money in the browser. Weights and percentages are computed server-side too.

The rule is narrower than "every percentage is a string", and the alert threshold is where that shows. A figure the **server computes** goes out as a string, because that is where precision is lost when a browser parses it. A value the **user typed** arrives as a plain JSON number, because `JsonNumberHandling.Strict` is registered and a quoted number would be rejected outright — the server is about to parse it into a `decimal` either way. So a saved threshold is a number inbound, while a fired alert's change and endpoint figures are strings outbound.

**EF Core only — no raw SQL.** The brief permits raw SQL or a query builder and asks only for parameterisation, which EF Core makes automatic. Parameterisation is proven by a `DbCommandInterceptor` in the test fixture asserting that no user-supplied value ever reaches `CommandText`.

**Frontend: zero external UI component libraries.** No Radix, Headless UI or React Aria — the brief bans UI kits and its list ends in "тощо". Hand-build with Tailwind; use native `<select>` and `<input role="switch">`.

**Tests.** Nothing is skipped and nothing ever should be, so passing and discovered stay equal. **785 passing, nothing skipped, of 785 discovered**, from one `dotnet test` run with Docker up: unit (touch no infrastructure), architecture (reflection over assembly references), integration (Testcontainers Postgres + Redis, one collection fixture for the assembly, needs `public partial class Program;`). Use `FakeTimeProvider` for anything timer-driven.

| Assembly | Passed |
|---|---|
| `Shared.Kernel.UnitTests` | 21 |
| `Modules.Identity.UnitTests` | 15 |
| `Modules.Portfolio.UnitTests` | 115 |
| `Modules.MarketData.UnitTests` | 239 |
| `Modules.Alerts.UnitTests` | 108 |
| `Architecture.Tests` | 60 |
| `Api.IntegrationTests` | 227 |

The browser tests are counted separately by `npm --prefix src/Web test` — **79 passing across 15 files**, measured on 2026-08-07. These are the only test counts in the repository; do not copy them into another document.

**Nothing skips, and `Identity.Contracts` is why that took work.** It is empty on purpose — nothing depends on Identity at runtime, so its `.Contracts` has nothing to hold. An empty assembly has its project references trimmed out of the metadata by the compiler, so a rule walking them finds an empty list and reports green. That used to be handled by skipping the two rules over it. It is now handled by **not generating a case at all**: `ScannedAssemblies` and `AssembliesFor` filter on `SolutionAssemblies.HasCode`, computed at run time. Add a type to `Identity.Contracts` and both rules switch themselves back on with no edit — verified by doing it, which took the architecture suite from 60 tests to 62.

`EmptyShells_AreExactlyThePhasesNotYetBuilt` hard-codes the list of assemblies no rule runs over, so that filter can never become a quiet hole: the same experiment turned it red. **Read the list from that test, never from here.** `MemberData_NamesTheLayerEachRuleClaims` derives its expected count the same way, so a rule pointed at the wrong layer is still caught.

**A rule that reports green over an empty assembly is not a rule, and Phase 4 proved it the expensive way.** Creating Alerts' five projects empty put seven more assemblies in that state, and it fell back one layer at a time as each was filled. In between, rule 1 was green over `Alerts.Application` and proved nothing. Do not read a green rule over a layer you have not populated as evidence of anything.

**A test that cannot fail is worse than no test**, because it reads as enforcement. Every architecture rule was checked by deliberately breaking it and watching it go red — that is how `PresentationAssemblies => AssembliesFor("Infrastructure")` was found, a copy-paste that pointed one rule at the wrong layer while still reporting green. `ReferenceWalker_FindsEdgesThatDoExist` protects against the same class of mistake permanently: a rule that passes by finding nothing needs a companion test that fails if the search finds nothing.

**Harder to spot: a test that can fail, but not on the mistake it exists to catch.** `Dashboard_ProviderReturns429_Returns200NotError` fails one of three symbols and asserts that the two served ones came back with their prices. That passes — and it passed *identically* under the implementation Phase 3 rejected, the single `try { provider } catch { redis-for-everything }` that throws away seventeen good prices because three failed. The good prices had already been written to `marketdata:last:*` earlier in the same request, so the wrong implementation re-read the numbers it had just stored: same symbols, same amounts, same count. The only visible difference was the `IsLastKnown` flag, and the test did not look at it. It now asserts `IsLastKnown == false` on the served symbols. **Ask not "can this test go red?" but "can it go red on the specific mistake it is named after?"** — a fallback path that reproduces the happy path's numbers is invisible to any assertion about numbers alone.

Phase 4 met the same shape twice more. A price-window member carries its timestamp as well as its price, because a sorted set silently overwrites a duplicate member — encode the bare price and a ticker that hits the same value twice erases its own earlier reading, while four sibling tests asserting on prices and ordering all stay green. Only a test that counts members catches it. And in `EndpointMetadataTests`, deleting `MapMarketDataEndpoints` does **not** move the derived module count, because `AddMarketDataApi` keeps that assembly loaded — so the derived set is what catches a missing `Map`, and the separate count assertion is the only thing that would catch a module wired nowhere at all. Raise that count when a module lands; never soften it to "non-empty".

## Where Identity is not a safe template

Identity was the only built module for a phase and a half, so it is what gets copied. It has no background service, no outbound HTTP, no pushed messages, no value object and no dependency on another module — five of its answers are wrong elsewhere, and each one fails quietly. Decide these before writing a module, not after.

| Identity does | Wrong for | Decide instead |
|---|---|---|
| Repositories save their own changes | Nothing today — Portfolio does the same. The question still recurs whenever two writes must land together | State the save point in the repository interface's doc comment before the first handler exists |
| Validates all config eagerly in `Add<M>Module` | MarketData. A missing `Finnhub__ApiKey` is a *supported* state, so `FinnhubOptions.FromConfiguration` does not throw and `AddMarketDataModule` uses `FakeQuoteProvider` instead. Eager validation there would take down `docker compose up`, which is the P0 gate | Validate eagerly only what the module genuinely cannot run without |
| Value converters are only for ids | Portfolio's `Ticker`, and all six of Alerts'. Identity has no value object that is not an id, so "id" and "type needing a converter" happen to name the same set. Alerts needed one converter per id, one per value object, **and** an enum-to-string for the alert direction — the count is only ever found by listing the mapped types | A converter for every custom mapped type; register both `Properties<T>()` and `DefaultTypeMapping<T>()` |
| Canonical form is a `public static` on the entity (`User.NormaliseEmail`) | Anything with a value object. Email is a bare `string` with nowhere else to live; `Ticker` canonicalises in its own factory | Canonicalise in the value object where there is one; a static on the entity only for bare primitives |
| Two host wire-ups finish a module | MarketData needs three (`AddMarketDataModule`, `AddMarketDataApi`, `MapMarketDataEndpoints`) — and the count is not the interesting part. It injects `IConnectionMultiplexer`, which `AddStockPortfolioRedis` registers, and nothing in the module says so. Delete or reorder that one line in `Program.cs` and the dashboard fails on the first request rather than at startup. Alerts needs the same three **plus two host adapters, a startup consistency check, and `AddSignalR` with its Redis backplane**, none of which any module declares — and its publisher is registered by `AddAlertsApi`, not `AddAlertsModule`, because `IHubContext` is ASP.NET Core and `.Infrastructure` may not reference it | Count the wire-ups the module needs **and the host services it silently assumes**. `Add` + `Map` does not mean wired |

## Traps

Each of these costs a day if you meet it cold.

- **`HasDefaultSchema` does not move `__EFMigrationsHistory`** (efcore#24127, closed *not planned*). Every context needs `MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")` or all three share one table and corrupt each other's bookkeeping. Never put `SearchPath=` in a connection string.
- **A constructor whose parameter names match mapped properties is picked up by EF when it loads rows.** Binding is by convention and cannot be configured. That is *fine and intended* here — the single private all-args constructor only assigns. It becomes a trap the moment a validation check is added inside it, because EF then re-runs that check on every row of every `SELECT`. Checks belong in the static factory.
- **EF binds a complex type's *own* constructor when loading rows, exactly as it does an entity's.** The rule above therefore covers **value objects too, not just entities** — and `Money` already breaks it: `Money(decimal, string)` calls `currency.ToUpperInvariant()`, so that allocation runs on every row of every `SELECT`, confirmed from a stack trace. Accepted for Phase 2 (three columns, one row per position) and recorded here as a known cost, not as good practice.
- **Projecting a `ComplexProperty` translates fine — project its members anyway, for a different reason.** Complex types translate to their constituent columns, and the one documented restriction is projecting one through an *optional* navigation, which the dashboard read does not do. `HoldingQueries.GetVisibleHoldingsAsync` projects `h.AveragePrice.Amount` and `.Currency` and rebuilds `Money` afterwards **to keep `Money`'s constructor off the per-row load path** — the `ToUpperInvariant()` cost in the entry above.
- **A `ComplexProperty` cannot be a constructor parameter** ([efcore#31621](https://github.com/dotnet/efcore/issues/31621), open — milestoned `11.0.0` in Feb 2026, then pushed back to `Backlog` with a `blocked` label in Jun 2026, so it is not arriving). `Holding` maps `Money AveragePrice`, and `private Holding(…, Money averagePrice, …)` fails model building. **That failure happens at model build, i.e. host startup — not on the first query**, so it takes the whole process down rather than one request. It does **not** require a parameterless constructor: EF sets properties it could not pass to the constructor afterwards. Omit only the complex member; the factory assigns it, since `private set` is reachable from inside the type.
- **A complex type's members are not mapped automatically if they have no setter**, and `Money.Amount` / `Currency` are get-only. A bare `builder.ComplexProperty(h => h.AveragePrice)` therefore maps nothing, cannot bind `Money`'s constructor, and throws *"No suitable constructor was found for the type 'Holding.AveragePrice#Money'"* at model build. Map each member explicitly inside the lambda — which is required anyway, because Npgsql does not snake-case `avg_price_amount` for you. The cost: **a `Money` member added later is silently unmapped** until someone adds a `.Property()` line, so a test asserts the member count.
- **`PropertyAccessMode.PreferField` is the default**, so EF writes the backing field and never calls your setter. Validation in a setter silently never runs — which is moot now that entities have no settable properties, but it is why they don't.
- **`Maximum Pool Size=2` on every connection string.** Azure Postgres B1ms allows 35 user connections, and a different username is a different Npgsql pool. **Count what opens a pool, not what is defined.** `db/init/01-roles.sql` creates five roles (`migrator` plus four `*_svc`) and four schemas, and since Phase 5 `Program.cs` registers **four** `DbContext`s — Identity's, Portfolio's, Alerts' and MarketData's — so **four Npgsql pools exist per replica**. At `Maximum Pool Size=2` and `maxReplicas: 2` that is 4 × 2 × 2 = **16** of 35, and `migrator` runs as a separate job rather than alongside the API. The default of 100 would make it 800. PgBouncer is unavailable on Burstable. **Count `AddDbContext` calls; this figure has been published wrong before.**
- **An unhandled exception in a `BackgroundService` kills the host** (`StopHost` is the default). The poll loop needs a `try/catch` inside the loop, and a second one per ticker — "a failed observer must not stop the next ticker being sampled" is enforced by that inner catch and by nothing else, whatever the interface's comment says.
- **`TryAdd` does not make a default lose to the host's override.** `TryAddSingleton`/`TryAddScoped` skip when the *service type* is already registered, and a module's `Add<M>Module` always runs **before** the host registers its adapters — so the module's `TryAdd` always wins the race to be first and the placeholder is always what lands. What actually makes the host's adapter win is last-registration-wins, plus the host using a plain `Add`. Write `TryAddScoped` for the host-supplied port and the no-op stays in force: nothing polls, nothing evaluates, no alert ever fires, and nothing fails. `Module_HostRegistersAnObserverAfterwards_TheHostsWins` pins it.
- **A singleton background service must not capture scoped or transient services.** The poller takes `IServiceScopeFactory` and resolves its ports, and `IQuoteProvider`, from a per-cycle scope. With a real key `IQuoteProvider` is a **transient typed `HttpClient`**; injecting it into a singleton pins one `HttpClient` for the life of the process and defeats handler rotation, quietly and only in production.
- **Two poll locks, and releasing the wrong one re-opens the gap it closed.** A per-minute claim key picks one winner within a minute; a separate in-flight key stops a cycle that overran from being joined by the next minute's cycle on another replica, because the first key says nothing across minute boundaries. Acquire claim first, then in-flight, and **release only after a successful acquire** — releasing after a *refused* claim deletes the winner's in-flight key. `Cycle_LeaseRefused_DoesNotEvenAskWhatToPoll` pins it.
- **`FakeTimeProvider` plus `BackgroundService` has a startup race.** A single `Advance(interval)` after `StartAsync` loses the tick: `PeriodicTimer` does not buffer one that arrives before the service registered its wait, and neither that moment nor the end of a cycle is observable from outside. Advance in steps until the expected cycle count is seen, rather than advancing once and asserting.
- **A generated migration does not compile in this repo.** `CA1861` under `TreatWarningsAsErrors` rejects the inline `new[] { … }` EF emits for a composite index — three of them in the Alerts migration, including the `descending:` array — so every generated migration needs the same hand-edit, and **regenerating one breaks the build until the edit is re-applied**. The Portfolio migration carries the identical edit and no document mentioned it for two phases.
- **A `0` placeholder in `appsettings.json` is not a placeholder for a limit.** Configuration overrides the code default, so a `0` written where a real value is a secret-free cap silently rejects everything a user could ask for. Leave a genuine value in `appsettings.json` for anything that is not a secret, and have the module fall back to its code default on a missing or non-positive reading. Do not "tidy" such a value back to a placeholder for symmetry with its neighbours.
- **EF cannot see inside a value-converted type**, so a projection of one of its members does not translate. Reading the distinct tickers out of a table whose ticker column is a converted value object is two steps — materialise, then select the inner value — not one query. It fails at runtime, not at model build.
- **Assigning a `DelayGenerator` silently turns off `Retry-After` handling**, which is on by default. `ShouldRetryAfterHeader` is not a flag — its setter *is* the `DelayGenerator` assignment, and `MaxDelay` is ignored for generated delays. Note that honouring `Retry-After` is a Polly behaviour, not a Finnhub one. **Phase 5 deleted the client-side token bucket** — it was sized to one provider's free tier and the brief says free-tier limits are not a problem — so this header, the retries and the breaker are now the only pacing there is. That makes this trap load-bearing rather than incidental.
- **`AddStandardResilienceHandler`'s defaults do nothing useful, and its validator can stop the host from starting.** `CircuitBreaker.MinimumThroughput` ships at 100, so the breaker can never open for a twenty-ticker dashboard — it is configured down to 10. And `HttpStandardResilienceOptionsCustomValidator` registers with `AddOptionsWithValidateOnStart`, so `AttemptTimeout > TotalRequestTimeout`, or `SamplingDuration < 2 × AttemptTimeout`, takes the host down at startup — which takes down `docker compose up`, the P0 gate. The shipped values satisfy both (5 s < 15 s; 30 s ≥ 10 s).
- **Finnhub's `/quote` cannot tell a non-existent symbol from a healthy one that blipped.** Both come back as `c: 0` — present, zero — so a check written as "`/quote` returned a non-null, non-zero `c`" answers *unknown* to every temporary failure and would permanently reject a valid holding after one bad second. The check is `/search?q=` with an **exact case-insensitive match on `result[].symbol`**, never `count > 0`, because `/search` matches company names as well as symbols — `q=appl` returns Applied Materials, Applovin and Science Applications International beside Apple, all verified against the live provider on 2026-08-06. Do not use `q=AAP` returning `AAPL` as the example: it is the one every earlier draft of this file used and it is **false** — that query returns `AAP` and `AAPJ` and no Apple at all. A null search result means the provider could not answer, and the check then **says yes** — a Finnhub outage must not reject a purchase. The only primary evidence is [Finnhub-API#54](https://github.com/finnhubio/Finnhub-API/issues/54), intermittent all-zero responses for AAPL/TSLA/FB. Entitlement failures come back as **401 or 403**, both with the same body, and neither is ever retried.
- **BYOK key verification is the one `IQuoteProvider` method that must not fail open, and `FakeQuoteProvider` needs its own invented rule to prove it.** Every other method on the interface fails open — a provider outage must not block a purchase — but `VerifyKeyAsync` is checking the key itself, so an unanswerable check must never be read as valid. With no `Finnhub__ApiKey` configured the fake is the *only* provider there is, so if it accepted every candidate key the rejected and unanswerable arms of the whole feature would be unreachable in every test and every local demo. The rule it uses instead — accept sixteen characters or more, reject anything shorter and the literal `"unknown"` sentinel, never answer `Unknown` at all — is invented behaviour with no source of truth outside the code, which is exactly the kind of thing a later pass "simplifies" back to always-true. There is deliberately no `Unknown` verdict from the fake: a fake that pretends the provider is down is a fake nobody could reason about, so length is the only axis it can say no on.
- **The alert feed is SignalR, and three of its settings are load-bearing together.** The browser is pinned to **WebSockets only** *and* **skips the negotiate step**, because that pair is the documented exemption from sticky sessions with a Redis backplane — it needs both halves, so allowing a fallback transport silently reintroduces the requirement and a browser routed to the wrong replica gets no alerts. The hub is mapped at `/api/alerts/stream`, which is the prefix nginx already forwards upgrades on. SignalR's own keepalive (about every 15s) is what keeps the connection under the platform's four-minute idle close; there is no hand-written heartbeat any more.
- **`Clients.User(...)` matches a claim these tokens do not carry.** The built-in provider reads `nameidentifier`; this app issues `sub`. Without `SubjectClaimUserIdProvider` every alert is delivered to nobody, with no exception and no log line — `AlertStreamTests` pins both the registration and the claim name.
- **A browser cannot set a header on the hub connection**, so SignalR's client sends the token as `?access_token=`. `BearerTokenEvents.OnMessageReceived` reads it, and the `StartsWithSegments` path check is what stops every other route in the app accepting a credential in its URL.
- **ACA liveness must not check Postgres or Redis.** A brief dependency failure then becomes a container restart loop, turning a degraded app into a down one.
- **Three probes, three questions, and every check must be tagged or it silently joins readiness.** Liveness runs zero checks; readiness runs the tag `"ready"` — the four database logins and the cache; startup runs the tag `"startup"` — pending migrations only, which is a database round trip and is exactly why it cannot be liveness. `MapHealthChecks` with no predicate runs **everything**, so an untagged check added later joins readiness by accident and a probe quietly changes meaning. There is a fourth map, `GET /api/health/detail`, authenticated, tag `"detail"`, and it is a `MapHealthChecks` rather than a hand-written route on purpose — a hand-written one makes the host the place every new component has to touch.
- **Readiness must never read a cache outage as an inability to serve traffic.** Every check registers with a default failure status of Unhealthy, so Redis down would answer 503 on `/health/ready`, Container Apps would withdraw the replica, and at `maxReplicas: 2` the whole API goes unreachable — from a *cache* being down, when the dashboard does not even read the cache on the happy path. Register it with `failureStatus: HealthStatus.Degraded`; the framework already maps Degraded to 200. The only test that catches this boots a host with Redis down and asserts `/health/ready` is **200**.
- **A health route must never answer 503 because the database is down.** Every database-touching route gained a 503; the health routes are carved out by hand. `/api/health/detail` maps all three states to 200. An endpoint whose job is to report that Postgres is unhealthy has to say so in a body — otherwise the browser's health card goes blank at the exact moment it becomes useful, and the alerts-suppressed banner, which reads the cache entry out of that same body, stops working whenever Redis and Postgres are down together.
- **Container Apps caps `failureThreshold` at 10 and `initialDelaySeconds` at 60**, and the obvious startup-probe numbers are refused at deployment validation, not at run time. Ten failures on thirty-second periods is five minutes and is inside both limits. `successThreshold` must stay 1 for a startup probe.
- **`InvariantGlobalization` is on, so a time-zone id resolves on one operating system and not the other.** `Directory.Build.props` sets it, which removes ICU. Measured, not assumed: the Linux container then resolves `America/New_York` and throws on `Eastern Standard Time`, and a Windows machine does the exact opposite. So look up the first, catch `TimeZoneNotFoundException`, look up the second. **`TryConvertWindowsIdToIanaId` is not a way out** — it is one of the calls invariant mode disables. Do not hand-roll daylight-saving arithmetic instead; `TimeZoneInfo` carries the real rules and any future change to them.
- **TanStack Query v5.89.0 renamed the mutation callbacks' `TContext` generic to `TOnMutateResult` and *added* a new `context` (`{ client, meta, mutationKey }`) as the last parameter of each**; `mutationFn` gained a second argument. **Argument positions did not move** — the `onMutate` snapshot is still argument 3 in `onError`/`onSuccess` and 4 in `onSettled` — so rollback code written before 5.89 still compiles and still restores the correct value. Pinned version is 5.101.4.
- **Tailwind v4 has no config file.** `darkMode: 'class'` does not exist; dark mode is `@custom-variant` in CSS. The failure is silent — `dark:` classes just never apply.
- **Tailwind v4's `@theme` snapshots a variable's value unless you write `@theme inline`.** Without `inline` a colour freezes at whichever scheme was active when it was read, and light/dark stops following `data-theme`. Nothing errors.
- **A page-wide token refresh must be one shared promise, not one per request.** When the access token expires every query on the page comes back 401 in the same tick. Without a single in-flight refresh, each one posts to `/api/auth/refresh`, the server rotates the refresh token once per call, and every response but the last carries a token the server has already retired — so the user is signed out at random, and only under load.
- **TanStack Router does not type-check a link target held in a plain `string`.** `ToPathOption` short-circuits on `string extends TTo ? string : …`, so only an inline literal `to="/somewhere"` on a `<Link>` is checked. A nav list whose `to` is declared `string` compiles happily with a route that does not exist and 404s at runtime — check every such entry against `routeTree.gen.ts` by hand.
- **Firing one optimistic mutation per row in a loop makes them all share one snapshot.** Each `onMutate` snapshots the whole list before applying its own change, so several started without awaiting capture the same pre-loop list, and one failed request rolls back every sibling that had already succeeded. Apply all the optimistic changes in a single `setQueryData` instead.
- **`setQueryData` on a query nothing has fetched yet makes it look fresh.** It stamps `dataUpdatedAt`, so the query stays fresh for its whole `staleTime` and never fetches. Push a live alert into an unseeded history cache and the panel shows that one row and no history at all. Write into the cache only when it already holds something.
- **The browser's `storage` event fires in every tab except the one that wrote.** That is the whole cross-tab sign-out mechanism: removing the token key tells the others and nothing else is needed. Do not react to a *changed* token — two tabs would then race to spend a single-use refresh token. The query that re-identifies the user after a sign-in elsewhere needs `staleTime: 0`, or the global 30s default hands back the identity already cached in this tab, the new token is never exchanged, and the tab sits "signed in" with no access token.
- **`//evil.example` gets past a `startsWith('/')` check.** The browser reads a protocol-relative URL as an absolute one, so a `?redirect=` guard written that way sends a freshly signed-in user to somebody else's site. A backslash does the same wherever a parser normalises it to a slash. Accept only same-origin, path-absolute targets.
- **SignalR's default reconnect gives up after four attempts.** Roughly two minutes of outage and the browser stops trying for ever — no error, no log line, the badge still claiming a connection. The client must supply its own retry list that never runs out.
- **SignalR asks for the access token before every request it makes, including each reconnect attempt.** Hand back whatever token is already in hand and a reconnect after expiry 401s for ever, so the factory has to refresh rather than return.
- **`useSuspenseQuery` throws on error by default**, so any rejected read lands in TanStack Router's built-in error panel — no shell, no nav, no retry — unless the route declares its own error component. `invalidateQueries` from a mutation's `onSettled` reaches it too, so a failed write can tear the page down a moment after the optimistic rollback repaired it.
- **`useSyncExternalStore` demands the same object back every time it asks.** Return a freshly built snapshot on each call and React re-renders for ever; keep one frozen object and swap it only on a real change.
- **jsdom applies no CSS, so a layout hidden with `display: none` is still findable in a test.** The desktop table and its mobile-card twin both match a text query, so every such assertion is ambiguous until the query is scoped to one of them.
- **Data Protection keys must be persisted to Postgres**, or every ACA revision leaves stored BYOK ciphertext unreadable.
- **ASP.NET Core listens on 8080**, not 80. `targetPort: 8080` in Bicep.
- **React 19 StrictMode runs effects twice.** The alert hook's cleanup must call `stop()`, or you hold two of the browser's six connections per origin. SignalR rejects a `start()` on a connection already stopped, which is what absorbs the torn-down first pass.
- **`docker-entrypoint-initdb.d` passes no `-v` to psql.** A `.sql` file using `:'password'` variables is a syntax error and, with `ON_ERROR_STOP=1`, aborts init — so `docker compose up` fails from a clean clone. Wrap it in a `.sh` that supplies the variables.
- **`CREATE SCHEMA … AUTHORIZATION migrator` needs `GRANT migrator TO CURRENT_USER` first.** Compose runs as superuser so it passes locally; the Azure Flexible Server admin is not a superuser and the migration job fails on first deploy.
- **`beforeLoad` is synchronous; React effects run after the first render.** Load the session *before* mounting `RouterProvider`, or a hard refresh of a protected route always bounces to `/login` — which is the session-persistence requirement failing while every test passes.
- **Vite `base` must come from the environment**, not be hardcoded to `/<repo>/`. nginx serves the compose SPA at `/`, so a baked-in base makes it request `/<repo>/assets/*.js` and render blank.
- **ACA adds default TCP probes when ingress is on.** Declare all three `httpGet` probes in Bicep — liveness, readiness and startup — or `/health/live`, `/health/ready` and `/health/startup` are never called and the split does nothing.
- **`OneOf.Types.NotFound` collides with `Microsoft.AspNetCore.Http.HttpResults.NotFound`.** Only affects a file that imports `HttpResults`, which endpoints no longer need now that they return `Task<IResult>`. If one ever does, alias it — `using NotFound = OneOf.Types.NotFound;` — rather than writing the full name at each use.
- **`[GenerateOneOf]` crashes on types in the global namespace.** It builds the generated filename from the namespace and emits `<global namespace>_Foo.g.cs`; `<` is illegal, so the generator throws `CS8785` and every implicit conversion then fails with unrelated-looking errors. Nothing uses the attribute now — handlers return `OneOf<…>` directly — but if one is ever reintroduced, declare it inside a namespace.
- **Revoking and rotating a refresh token are not the same ending.** Both stamp `SupersededAt`; only rotation sets `SupersededBy`. A grace-period check written against `SupersededAt` alone therefore keeps accepting the token the user just logged out with, for the whole window — logout silently does nothing for 30 seconds while every test stays green. `Refresh_AfterLogout_IsRejectedInsideTheGraceWindow` pins it.
- **`OneOfDiagnosticSuppressor` does not exist on nuget.org** and is not needed. `.Match` takes one delegate per case, so every case must be handled — adding a case breaks every call site. `CS8509` only fires if you `switch` over `.Value`, which the convention forbids anyway.
- **`CA1707` makes every `Method_Scenario_Expectation` test a build error** under `TreatWarningsAsErrors`. `tests/Directory.Build.props` suppresses it — and must explicitly `<Import>` the root props, because MSBuild only auto-imports the first `Directory.Build.props` it finds walking up.
- **`GetPathOfFileAbove` inside `Exists(...)` fails to parse** with `MSB4092` — the nested single quotes break the condition parser. Put the path in a property first, then condition on the property.
- **EF needs no parameterless constructor — but it binds by NAME.** Constructor binding has existed since EF Core 2.1 and does not care about accessibility, so one private all-args constructor is enough. The hazard is renaming a constructor parameter without renaming its property: EF then finds no constructor it can use, and with no parameterless fallback the **whole model fails to build at startup**, not on the first query. `EfConstructorBindingTests` pins it.
- **An assembly-level `[SuppressMessage]` must live in its own `AssemblyInfo.cs`.** Twice now, deleting an unrelated type (`AggregateRoot.cs`, then `IEndpointModule.cs`) took the assembly's `CA1716` suppression with it and broke the build, because the attribute was sitting in whichever file happened to be first.
- **`dotnet test --no-build` after a FAILED build silently runs the previous assembly** and reports green. A mutation test "passing" means nothing unless the build before it succeeded — check the build result, not just the test result.
- **Regex renames rewrite more than types.** Renaming `RefreshSession` → `RefreshSessionCommand` across the repo also rewrote the *namespace* segment and an OpenAPI operation id inside a string. The namespace matches the **folder** and carries no role suffix; text in strings is not an identifier. The build failed on two unrelated-looking XML `cref` errors, two steps from the cause.
- **Scripts that filter by file extension miss `Dockerfile`** — it has no extension. A repo-wide rename left both .NET images copying `*.Presentation.csproj`; `dotnet build` stayed green because those paths only exist inside the container build context.
- **A local `dotnet run` holds file locks** and breaks the next build with `MSB3021: being used by another process`. `pkill` does not reach it on Windows — use `Stop-Process`.
- **Windows Application Control can block a freshly built DLL** with `0x800711C7 — An Application Control policy has blocked this file`. It shows up as a `FileLoadException` in **`Architecture.Tests`**, because that is the suite that reflects over every assembly — but the blocked assembly is the one **named in the exception message**, not the test project, and only the few cases that touch it go red. Not a code fault. Delete the **named** project's `artifacts/bin` and `artifacts/obj`, then rebuild — deleting is the point, since an incremental rebuild alone leaves a DLL whose inputs have not changed exactly where it is.
- **`Microsoft.OpenApi` must stay on 2.x.** 2.0.0 carries GHSA-v5pm-xwqc-g5wc so pin ≥2.11.0, but 3.x makes `IOpenApiMediaType.Example` read-only while the ASP.NET Core OpenAPI source generator still assigns to it (`CS0200`).
- **A new pay-as-you-go subscription has almost no resource providers registered**, and the obvious pre-flight check does not catch it: `az provider show -n Microsoft.App --query "…locations"` happily returns a region list for a provider the subscription may not use. Being available and being allowed are different questions. The deploy dies at the first resource with `MissingSubscriptionRegistration`. Six needed registering here — `App`, `Cache`, `ContainerRegistry`, `DBforPostgreSQL`, `ManagedIdentity`, `OperationalInsights`. Only `Authorization` was already registered.
- **`appLogsConfiguration: { destination: 'none' }` is rejected at preflight** with *"App Logs destination 'none' not supported. Supported values: 'log-analytics', 'azure-monitor' or none"* — a message that lists as valid the exact value it just refused. The trailing "or none" means **the property omitted**, not the string. Leave the block out entirely.
- **An `existing` resource plus `listKeys()` creates no dependency on the module that builds it.** `main.bicep` declared the Redis cluster as `existing` and read its key. ARM ran the key lookup early anyway, and a first deploy into an empty group failed with `ParentResourceNotFound` — then "passed" on retry, because by then the cluster existed. Build the connection string **inside** the module where the resource is really created, and return it as a `@secure()` output.
- **GitHub now issues immutable OIDC subject claims.** The federated credential subject is `repo:<owner>@<ownerId>/<repo>@<repoId>:ref:refs/heads/main`, not the `repo:<owner>/<repo>:…` form every guide still documents. The old form matches nothing and fails with `AADSTS700213`. Read the subject out of the failing run's log and register that exact string; registering both forms is harmless.
- **A Postgres container healthcheck needs both `-U` and `-d`.** Without them `pg_isready` checks a default user and database that are not the ones the app uses, so it reports healthy while the real database is not ready. The API then starts too early and fails its first query.
- **Shell scripts mounted into a Linux container must have Unix line endings.** A Windows checkout gives them carriage returns, and the container reports a "file not found" naming a file that is plainly there — the interpreter is looking for a filename with an invisible character on the end. Force LF for `*.sh` in `.gitattributes`.
- **Postgres 18 moved where it stores data.** A volume mounted at the old path silently gets a fresh empty database on every start, which looks like data loss but is actually two different directories.
- **A configuration class with constructor parameters is skipped in silence.** The binder needs a parameterless way in; give it one, or the settings arrive empty with no error anywhere.
- **Copy the shared build settings file before the project files in a container build**, or every project restores against different settings than it compiles with.
- **Run the container as an explicit non-root user.** The base image does not do it for you.
- **The web server needs streaming turned off for the alert feed.** Its default is to buffer a response until it is complete, which for a stream that never completes means the browser receives nothing at all.
- **Do not add a helper for the 403 response until a route actually returns 403.** A helper with no caller is a shape somebody will fill in wrongly.

## Deployment

📄 **[docs/DEPLOYING.md](docs/DEPLOYING.md) is the runbook; [the design record](docs/superpowers/specs/2026-08-02-azure-deployment-design.md) is the why. Read the runbook before any deploy work.** This section is the summary; those files have the procedure, the cost model, the four decisions, the six-step verification and the six failed attempts.

Three targets: `docker compose` (whole stack, local, the P0 gate), **GitHub Pages** (SPA, static, `VITE_API_BASE_URL` baked in at build), **Azure Container Apps** (API only).

**Deploying means pushing to `main`. Nothing else.** `deploy.yml` fires on push to `main` or on manual trigger, and installs Bicep and runs `az deployment group what-if` inside the runner. Having `az` on your own machine only buys a rehearsal — a local `bicep build` and a local `what-if`. Not having it does not block a deploy.

`FINNHUB_API_KEY` is set, so the deployed app serves genuine prices. The secret is optional and defaults to empty. Empty means the public URL prices real tickers from a generated random walk, which reads as broken rather than as a thoughtful fallback. The empty path exists so that `docker compose up` works from a clean clone with no registration, and that is the only place it belongs.

⚠️ **`teardown.yml` deletes the whole resource group once the `deleteAfter` tag passes**, and a redeploy re-stamps it to today + 14 days. The last deploy set it to **2026-08-19**. Deploying extends the window by using it; not deploying lets it expire. An unreadable or missing tag also deletes — deliberate, since a group with no readable deadline is a group nothing is bounding.

`main.bicep` passes **`minReplicas: 1`**. It passed 0 through Phases 1–3, purely to cut cost on a personal pay-as-you-go subscription, and that was safe only while nothing needed a replica running between requests. Phase 4's quote poller ended it: a sleeping replica samples no prices, so no alert ever fires, and nothing anywhere reports a fault. **The poller and the always-on replica are one decision, not two** — reverting either alone gives a feature that silently stops working whenever traffic does. The rule that keeps them in step is now the other way round: while a `BackgroundService`, `PeriodicTimer` or `IHostedService` exists in `src/`, 1 is correct.

The scale rule's `concurrentRequests` is **400**, raised from 100 in the same change. A held-open stream may count as one in-flight request for its entire life, so at 100 a few dozen connected browsers would scale on *user count* rather than on load. `maxReplicas` stays at **2**, which is what the database connection budget allows.

The cost of that is a replica billed at the active rate around the clock — an open stream also never qualifies for the platform's reduced idle rate — instead of the cold start `minReplicas: 0` used to buy. The cold start is gone with it, which was the honest cost of the old setting: from zero, the first dashboard request paid container start **and then** the per-ticker fan-out, one after the other.

Cost is bounded by **time, not by budget**. Pay-as-you-go has no Azure spending limit, and a budget only sends email — it cannot stop anything. `deploy.yml` stamps a `deleteAfter` tag on the resource group and `teardown.yml` deletes the group once that date passes. Live deployment: resource group `stockportfolio-rg` in `polandcentral`, ~$1.26/day measured while the API still scaled to zero and higher from the first Phase 4 deploy onwards, API at `stockp-api-qdgz3wugqbihs.icysea-481b5825.polandcentral.azurecontainerapps.io`, SPA at `dilicidum.github.io/StockPortfolio`. Postgres Flexible B1ms and Azure Managed Redis Balanced B0 with HA off — **not** Azure Cache for Redis, which is retiring.

The SPA and the API are on different origins and always will be, so the alert hub authenticates from the query string rather than a header — which is SignalR's own answer to it, not ours. GitHub Pages needs `404.html` copied from `index.html` plus a Vite `base` and a matching router `basepath`.

## Deliberately not built

These were considered and cut. Don't reintroduce them without asking.

- **Alert replay** — no cursor, no message ids, no 24h backfill. Req 9 asks for an event on breach, a background check, and a simulate button. History is a plain `GET`; the hook re-runs that query from `onreconnected`, which is the whole of how the gap is closed.
- **Watchlist** — «перелік акцій» in req 8 sits inside *dashboard settings*, so it means which of your holdings show on the dashboard. That is `is_visible` on `holdings`.
- **A cached ticker table in MarketData** — the poll list is read live each cycle, from Alerts. Removing it also removed two event handlers, a reconciliation pass and a way for the two lists to disagree.
- **Raw SQL** — see Conventions.
- **Trading-hours gating** — dropped entirely. It existed to stop pointless polling outside market hours; the poller now only runs for tickers with an active alert, and the dashboard fetches on demand, so there is nothing to gate. Note that market hours came back for a *different* job in Phase 6 — counting how long a stored price has been allowed to age — and that is a read-side rule, not a gate on polling.
- **A market-holiday calendar** — a week of work for a demo. On Thanksgiving afternoon the price column dashes an hour into a market that was never open; the failure is cosmetic and corrects itself the next trading day.
- **Falling back to the fake provider when a key is rejected** — it would serve invented prices for real tickers on the deployed site, which is the one thing the fake must never do. The app starts, reports the rejection through the health detail, and keeps serving last-known prices instead.
- **A hand-written alert stream** — server-sent events, a single-use connection ticket and a hand-rolled reconnect were all built and then deleted in favour of SignalR over WebSockets, which is what the brief lists. Roughly 450 lines of our code became about 60. Do not reintroduce any of it: the ticket, the heartbeat and the backoff table are all things the library does. The README carries the comparison; the UI badge says "Live (WebSocket)".
