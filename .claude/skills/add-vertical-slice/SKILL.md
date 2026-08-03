---
name: add-vertical-slice
description: Add one complete use case to a module — command or query, handler, failure records, request, validator, endpoint, DI registration and tests — in the order that keeps the build green. Use when implementing a new feature, use case, command, query or endpoint in Identity, Portfolio, MarketData or Alerts.
---

# Add a vertical slice

One use case, end to end. `<M>` is the module, `<Feature>` the feature area (Identity's is
`Authentication`), `<UseCase>` the operation (`RegisterUser`, `AddHolding`).

Read the root `CLAUDE.md` first — especially **"Where Identity is not a safe template"**, which lists the
five places Identity's answer is wrong for a module with domain events, a background service or an
outbound dependency.

## Before you start

Answer these. Each one is cheap now and expensive after the first handler exists.

1. **Command or query?** A query that changes nothing returns its result type directly, with no union.
2. **What are the outcomes?** One per thing the caller must handle *differently*. This becomes the
   `OneOf<…>` in the handler's signature, and adding one later breaks every `.Match` — which is the point.
3. **Does the commit point already exist for this module?** If this is the module's first write, decide
   whether repositories self-commit (Identity) or the handler's writes land in one outer transaction
   (Portfolio, because of event dispatch) and **write it on the repository interface's doc comment**.
4. **Does an existing collaborator already do part of this?** Identity's `SessionOpener` exists because
   three handlers were about to hold the same four invariants.

## Order

Build inward-out. Each step compiles before the next.

### 1 · Domain, only if the rules changed
`src/Modules/<M>/…<M>.Domain/`

New entity → private all-args constructor that only assigns, static factory returning
`OneOf<T, InvalidInput>`, mutators that change tracked state and do not persist. If it maps a complex type
(`Money`), omit that member from the constructor and assign it in the factory — see the Traps section of
the root `CLAUDE.md`.

Add the entity to the module's constructor-binding test in the same commit.

### 2 · Application
`…<M>.Application/<Feature>/Commands/<UseCase>/` — or `Queries/<UseCase>/`

```
<UseCase>Command.cs          record of primitives; no framework types
<UseCase>CommandHandler.cs   ICommandHandler<<UseCase>Command, OneOf<…>>
<Failure>.cs                 one file per failure case, in THIS folder
<UseCase>Result.cs           only if the success payload is unique to this use case
```

The suffixes are not optional. `<UseCase>Result` is the **success payload**, never the union. Failure
records never move to a shared `Errors.cs`.

Add any new seam to `Application/Abstractions/`. Ask context questions here (`Find…` → failure record),
not by reading a SQLSTATE in `.Infrastructure`.

### 3 · Infrastructure
`…<M>.Infrastructure/`

Implement new abstractions. `internal` — the module's only public type is `<M>Module`. Register the handler
in `DependencyInjection.cs` with the closed generic spelled out.

New entity → EF configuration, converters for any new converter-backed type (register **both**
`Properties<T>()` and `DefaultTypeMapping<T>()`), then a migration:

```bash
dotnet ef migrations add <Name> --context <M>DbContext --output-dir Persistence/Migrations --project src/Modules/<M>/StockPortfolio.Modules.<M>.Infrastructure --startup-project src/Api
```

### 4 · Api
`…<M>.Api/`

```
Requests/<UseCase>Request.cs             what binds off the wire
Validators/<UseCase>RequestValidator.cs  shape only — no I/O
```

Then the endpoint: `Task<IResult>`, `.Match<IResult>` with **every lambda parameter named**, the command
built with `new`. Route parameters stay their own delegate parameters. Declare every status the route can
emit; put a status on the group only when it is genuinely universal.

### 5 · Wire it
`src/Api/Program.cs` — `Add<M>Module`, `Add<M>Api`, `Map<M>Endpoints`. If the module is new, `src/Migrator/Program.cs`
too. Nothing enforces this; a missed `Map` builds, registers, passes every test, and serves nothing.

### 6 · Tests
Add to the existing projects — do not create new ones.

- **Unit**: the domain rule, and the handler if fakes exist for its seams
- **Integration**: the happy path plus every failure case the union declares
- **`EndpointMetadataTests`**: extend the theory with this route's `(call → status)` pairs

For each test, name the mutation that turns it red. If you cannot, delete it.

## Before you call it done

- [ ] `dotnet build` — 0 warnings (`TreatWarningsAsErrors` is on)
- [ ] `dotnet test` — quote passing **and** skipped; a rising skip count means a rule stopped asserting
- [ ] Every status the route emits is declared, verified against a real response — not from the source
- [ ] `/openapi/v1.json` names your `<UseCase>Request`, not the command
- [ ] It works in a browser or against `docker compose up`. A phase is done when it runs, not when tests pass.

## Do not

- Bind an `.Application` type off the wire
- Put a guard inside an entity constructor — EF runs it on every row of every `SELECT`
- Add `ConfigureAwait(false)`, a unit of work, or `[GenerateOneOf]`
- Use `_ =>` in a `.Match`
- Pool failure records into a shared errors file
- Copy Identity's commit point without checking "Where Identity is not a safe template" in root `CLAUDE.md`
