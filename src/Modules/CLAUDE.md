# Module rules

Loaded whenever a file under `src/Modules/` is read. Follow these when building Portfolio, MarketData or
Alerts. `Identity` is the built example — [Identity/CLAUDE.md](Identity/CLAUDE.md) says which file shows what.

Root `CLAUDE.md` holds the traps and the deployment/architecture decisions. This file is the code shape.

---

## Naming — not optional

```
<UseCase>Command          RegisterUserCommand
<UseCase>CommandHandler   RegisterUserCommandHandler
<UseCase>Query            GetCurrentUserQuery
<UseCase>QueryHandler     GetCurrentUserQueryHandler
<UseCase>Result           the SUCCESS payload — never the union
<UseCase>Request          the wire body, in .Api
<UseCase>RequestValidator beside the request
<Module>Module            the one public type in .Infrastructure
```

Never `RegisterUser` for a command or `RegisterUserHandler` for a handler.

## Folder structure

```
<M>.Domain/                       entities, ids, invariants
<M>.Application/
  Abstractions/                   interfaces .Infrastructure implements
  <FeatureArea>/                  e.g. Authentication, Portfolio
    Commands/<UseCase>/           Command · CommandHandler · <Failure>.cs · Result?
    Queries/<UseCase>/            Query · QueryHandler · Result
<M>.Infrastructure/
  <M>Module.cs                    * the only public type
  DependencyInjection.cs          handler registrations
  Persistence/                    DbContext · Configurations/ · Converters/ · repositories
<M>.Api/
  <M>Endpoints.cs                 * routes
  Requests/                       what binds off the wire
  Validators/                     one per request
```

Namespace matches the folder and stops at the use-case folder — `…Application.Authentication.Commands.RegisterUser`,
with no `Command` suffix on the namespace.

One file per failure record, in the folder of the use case that returns it. **No shared `Errors.cs`.**

## Domain objects are rich

- **One constructor**: private, takes every mapped value, assigns and nothing else.
- **No parameterless constructor, no object initialiser, no public setter.** The static factory is the only way in.
- **The constructor stays guard-free.** EF binds it by parameter name and uses it to materialise every row —
  a guard inside runs on every row of every `SELECT`. Guards go in the factory.
- **No base class.** Each entity declares its own `Id`. There is no `AggregateRoot`.
- `Create(...)` returns `OneOf<T, InvalidInput>`. Mutators change tracked state and **throw** on invariant
  violation. They do not persist.
- Ids are `readonly record struct <Entity>Id(Guid Value)` with `New() => new(Guid.CreateVersion7())`, mapped
  `ValueGeneratedNever()`.
- **`UserId` lives in `Shared.Kernel`**, beside `Money` — it is a framework-free value type every module needs.
  Each module maps it with its own converter in its own `.Infrastructure`.

**One complex type caveat:** a `ComplexProperty` (e.g. `Money`) cannot be a constructor parameter
([efcore#31621](https://github.com/dotnet/efcore/issues/31621)). Omit **only** that member — the rest still
bind — and let the factory assign it after construction. You do not need a parameterless constructor.

## OneOf

- A handler returns `OneOf<…>` of its outcomes **directly**. No `[GenerateOneOf]`, no named union class.
- `<UseCase>Result` is the success payload. Several use cases may share one (`TokenPair`).
- `Success` and `NotFound` come from `OneOf.Types` — never redeclare them.
- `InvalidInput` (in `Shared.Kernel`) is the one shared failure. Everything else is named per use case.
- Map with `.Match<IResult>(...)`. **Name every lambda parameter** — `emailTaken =>`, not `_ =>`.
- Never `switch` over `.Value`; that is the only way to lose exhaustiveness.

## Validation — three layers, one mechanism each

| Layer | Where | How |
|---|---|---|
| Shape — "is this even an email?" | request record, `.Api` | FluentValidation run by `ValidationFilter<T>` : `IEndpointFilter` → **400** |
| Context — "does this exist? allowed?" | handler, `.Application` | a `OneOf` result case |
| Invariant — "a User can never have a blank email" | entity, `.Domain` | **throws** |

- It is an **endpoint filter, not a DI decorator** — a decorator would have to manufacture a failure value for
  an unconstrained `TResult` and cannot.
- Inject `IValidator<T>`, **never** `IEnumerable<IValidator<T>>` — the collection form silently validates nothing.
- Validators do no I/O. "Is this email taken?" is a context question and belongs in the handler.
- Not the built-in `AddValidation()` — DataAnnotations-driven, awkward for conditional or cross-field rules.

## Handlers

- `ICommandHandler<,>` / `IQueryHandler<,>` injected straight into endpoints. **No dispatcher.**
- `.Application` references no `Microsoft.*` — only its `.Domain`, other modules' `.Contracts`, and `OneOf`.
- No `ConfigureAwait(false)`. No unit of work.
- No null guards where the compiler already answered.
- One clock reading per set of values that must agree with each other, from an injected `TimeProvider`.

## Endpoints

- Handlers return `Task<IResult>`.
- The endpoint binds `<UseCase>Request` and builds the command with `new`. **An `.Application` type never
  binds off the wire.**
- Route parameters stay their own delegate parameters — never inside the request record.
- Declare **every** status the route can emit. `.Produces` metadata is now the only description of what a
  route returns, so verify it against a real response, not against the source.
- The user id comes from the `sub` claim, not the body.

## Persistence

- `.Infrastructure` is `internal` except `<M>Module`.
- One `DbContext` and one schema per module, each connecting as its own role.
- `MigrationsHistoryTable(name, schema)` per context — `HasDefaultSchema` does not move it.
- Converters live in `.Infrastructure/Persistence/Converters`, never beside the type in `.Domain`.
  Register **both** `Properties<T>().HaveConversion<>()` and `DefaultTypeMapping<T>().HasConversion<>()`.
- EF Core only — no raw SQL.

## Tests

- **A test that cannot fail is worse than none.** For each one, name the mutation that turns it red.
- A rule that passes by *finding nothing* needs a companion that fails if the search finds nothing.
- Add to the existing test projects; do not create new ones.
- `FakeTimeProvider` for anything timer-driven.
- Extend `EndpointMetadataTests` with each new route's `(call → status)` pairs.
- Quote passing **and** skipped counts. A rising skip count means a rule stopped asserting.

---

## Where Identity is not a safe template

Identity has no domain events, no background service, no outbound HTTP, no SSE and no cross-module
dependency. Five of its answers are wrong elsewhere — decide each before writing the module:

| Identity does | Wrong for | Decide |
|---|---|---|
| Repositories self-commit | Phase 2 — event dispatch needs handler writes in one transaction | State the commit point on the repository interface before the first handler |
| Validates all config eagerly in `Add<M>Module` | Phase 3 — a missing Finnhub key is a *supported* state; eager validation breaks `docker compose up`, the P0 gate | Validate only what the module cannot run without |
| Converters are for ids | Phase 2 `Ticker`, Phase 4 `AlertDirection` | Converter for every custom mapped type, not just ids |
| Canonical form is a static on the entity | `Ticker` is a value object and canonicalises in its own factory | Static only for bare primitives |
| Two host wire-ups finish a module | Phases 3–4 add a poller, an adapter, Redis | Count the wire-ups the module actually needs |
