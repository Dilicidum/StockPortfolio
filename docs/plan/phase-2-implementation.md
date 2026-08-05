# Phase 2 — Implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: use `superpowers:subagent-driven-development` (recommended) or
> `superpowers:executing-plans` to work through this plan task by task. Steps use checkbox (`- [ ]`) syntax.
> Each task also follows the repo's own `.claude/skills/add-vertical-slice/SKILL.md` order; where the two
> disagree, this file wins because it names the actual files.

Companion to [phase-2-my-portfolio.md](phase-2-my-portfolio.md). That file says *what* Phase 2 must do and
which traps to avoid. This one says *which files exist, in which project, referencing what, built in which
order* — the same relationship `phase-1-implementation.md` has to `phase-1-sign-in.md`.

**Goal:** Add AAPL 10 @ $100, then AAPL 10 @ $150 → one row, 20 shares @ $125. Edit it. Delete it. Persisted,
and live on Azure.

**Architecture:** Portfolio becomes the second real module, built by copying Identity's five-project shape.
It adds two things Identity does not have and therefore cannot teach: a value object with a converter
(`Ticker`) and a complex-typed property (`Money AveragePrice`).

**Tech stack:** unchanged. No new NuGet package, no new npm package, no infrastructure change.

---

## 0.0 Mid-phase decision — Alerts is merged into Portfolio, and domain events are withdrawn

Taken while this plan was being executed, so parts of it below are stale by design rather than by neglect.
**Task numbering is preserved**: withdrawn tasks keep their numbers and are marked `WITHDRAWN` in place, because
execution is already keyed to them and renumbering would desynchronise it.

**The decision.** Alerts stops being a fourth module and becomes a feature area inside Portfolio. Three
modules: `Identity`, `Portfolio`, `MarketData`. Full reasoning in
[00-overview.md](00-overview.md) §"Three modules, not four"; in short, `Ticker` meant exactly the same thing on
both sides of the Portfolio/Alerts line, so ubiquitous language never diverged and there was only ever one
bounded context there. What does apply is subdomain classification: Portfolio-with-alerts is **core**, Identity
is **generic**, MarketData is **supporting**.

**What it withdraws from this plan:**

| Item | Status |
|---|---|
| §2.2's defence of declaring `Ticker` three times | **Reversed.** It is the exact pattern DDD's ubiquitous-language test rejects |
| §2.7 dispatch after the save commits | **Moot.** There is nothing to dispatch |
| §2.8's `HoldingRemoved` half, and `Portfolio.Contracts → Shared.Kernel` for `IDomainEvent` | **Withdrawn.** The two read interfaces survive; see §2.8 |
| Task 1 — domain-event types in `Shared.Kernel` | **WITHDRAWN** |
| Task 5 — the `HoldingRemoved` record | **REPLACED.** `HoldingRemoved` stays withdrawn. The two set-based reads are gone too — the poller's list comes from Alerts now and alert evaluation owns its own subscriptions. `Portfolio.Contracts` ships **one** interface, `IUserHoldsTicker`, so Alerts can reject a subscription for a ticker you do not hold |
| Task 10 — dispatch interceptor, publisher, 6 tests | **WITHDRAWN** |
| §7's "`Ticker` is declared three times" and "the sync `SavingChanges` throws" risks | **Withdrawn with the tasks that created them** |

`Shared.Kernel/DomainEvents/` is deleted, not merely unused: `IDomainEvent`, `IDomainEventHandler` and
`IDomainEventPublisher` are gone. Phase 1 wrote `IDomainEvent`, found nothing raised it and deleted it
(`phase-1-implementation.md` §5.2). `HoldingRemoved` was the only raiser in six phases and existed solely
because Alerts sat behind a module boundary. Reintroducing the abstraction with the raiser removed would have
repeated Phase 1's mistake one phase later.

**What Phase 2 still delivers is unchanged**: holdings CRUD, the merge arithmetic, `Ticker`, `Money`
mapping, the migration, the four use cases, the endpoints and the SPA route. Alerts themselves are still
Phase 4 work — they are just built inside Portfolio when they arrive.

---

## 0. Read this first — the Phase 2 spec is wrong in eleven places

`phase-2-my-portfolio.md` was written before Phase 1 existed. Ten of its statements do not survive contact
with the code that got built, and one is a factual error about a third-party library. Every one is corrected
below and carried into the tasks. **Do not work from the spec's §2 and §4 directly.**

| # | `phase-2-my-portfolio.md` says | Reality | Where fixed |
|---|---|---|---|
| 1 | §2.1 `private Holding(HoldingId id, UserId userId, …)` | `UserId` lives in `Identity.Domain`. Portfolio may reference only `Identity.Contracts`, which is empty, and `ModuleBoundaryTests` enforces it. Root `CLAUDE.md`: *"a module referencing a user it does not own stores a plain `Guid`."* | §2.1 |
| 2 | §4 *"the existing ACA migration job picks it up automatically because it runs a bundle over all four contexts"* | `src/Migrator/Program.cs` is not a bundle. It scans `ServiceDescriptor`s, and line 43 registers **Identity only**. Miss the line and the migrator prints "up to date", exits 0, and the API serves 500s against an empty schema — a P0-gate failure the spec claims is impossible. | Task 12 |
| 3 | §4 *"one new integration-test container dependency, which is already in the fixture"* | The *container* is there. The *wiring* is not: `ApiFixture.SettingsFor` sets `ConnectionStrings:Identity` only, `ApplyMigrationsAsync` migrates one context, and `ModuleDbContextInterceptors` has no `AddToPortfolio`. | Task 14 |
| 4 | §4 *"Infrastructure delta: **None**"* | `src/Api/appsettings.Development.json` has no `ConnectionStrings:Portfolio`. Every `dotnet ef` command builds the host, so a missing key breaks migration tooling before it breaks runtime. | Task 6 |
| 5 | §5 three SQL-injection tests | Two already exist. `ParameterisationTests.Queries_NeverInlineUserInput_IntoCommandText` is the recording-interceptor proof and `HostileInput_IsStoredVerbatim_AndExecutesNothing` *is* the proposed `Register_EmailContainingQuoteAndComment_RoundTripsExactly`. The fixture-wide interceptor the spec proposes building is already built. | §2.8, Task 14 |
| 6 | §3 *"`Table` (with the mobile card fallback from Phase 1)"* | No `Table` exists. `src/Web/src/components/` holds `Alert, AppShell, AuthLayout, Button, Card, Logo, Spinner, TextField`. Phase 1 shipped no table and no card fallback. | Task 16 |
| 7 | §3 *"TanStack Query v5.89.0 … rolls back the wrong snapshot"* | **False, and so is the matching `CLAUDE.md` trap.** v5.89 *appended* the new `context` and *renamed* the 4th generic; the `onMutate` snapshot stayed at position 3 in `onError` and 4 in `onSettled`. Pre-5.89 rollback code still compiles and still restores the right value. | §2.7, Task 20 |
| 8 | §2.3 use cases live in `Application/Portfolio/Commands/…` | That yields namespace `…Portfolio.Application.Portfolio.Commands.AddHolding`. Identity deliberately used a feature name that is not the module name. Use `Holdings/`. | §2.4 |
| 9 | §2.3 returns `HoldingSummary`; §2.4 returns `HoldingDto[]` | Two names, one type. It is `HoldingSummary`, and it sits at the `Application` root beside where `TokenPair.cs` sits in Identity. | §2.4 |
| 10 | §6 *"Catch `PostgresException` with `SqlState == "23505"` and map it to the merge path"* | Retrying into the merge path re-sends the identical `INSERT`, because a failed `SaveChangesAsync` skips `AcceptAllChanges` and the entity is **still `Added`**. The line as written is an infinite loop. Decision §2.6 removes the catch entirely. | §2.6 |
| 11 | §6 *"EF 11 is already deprecating the owned-JSON path"* | Unsourced. The EF 10 and EF 11 release notes say no such thing. `ComplexProperty` is still right, for the reason that *is* true — owned types carry identity. | §2.3 |

Two more, from `docs/plan/` rather than from the spec:

- **`phase-1-implementation.md` §3's package list is stale in five places.** It prints EF `10.0.7`,
  FluentValidation `12.0.0`, `xunit.v3 3.1.0`, OpenApi `10.0.0` and a `GlobalPackageReference` for
  `OneOf.SourceGenerator`. `Directory.Packages.props` has EF `10.0.10`, FluentValidation `12.1.1`,
  `xunit.v3 3.2.2`, OpenApi `10.0.10`, and no source generator at all. **`Directory.Packages.props` is the
  source of truth.** Never copy a version out of a plan file.
- **`phase-2-my-portfolio.md` §5's last two table rows are orphaned markdown** — lines 248-249 sit below a
  prose paragraph with no header above them, so they render as literal pipe text and are easy to miss.
  Both are addressed: one moves (§2.9), one is rewritten (Task 15).

---

## 1. Scope

Brief **P0 req 4** (portfolio CRUD), and the half of **req 6** (parameterisation) that a second module with a
second schema newly puts at risk.

Phase 2 does **not** build: prices, P&L, totals beyond `Invested`, visibility toggling, alerts, or any
background service. `minReplicas: 0` in `main.bicep` therefore stays correct for one more phase.

---

## 2. Decisions settled before any code

Nine of these are decisions the spec left open, got wrong, or never knew it had to make. Each was expensive
to reverse once forty files existed in Phase 1; the same applies here.

### 2.1 DECISION — Portfolio stores `Guid`, not `UserId`

`UserId` is `Identity.Domain`'s type. `ModuleBoundaryTests.Assembly_ReferencingAnotherModule_ReachesOnlyItsContracts`
makes reaching it a test failure, and `Identity.Contracts` is deliberately empty because nothing calls
Identity at runtime — the JWT carries the subject.

So `Holding.UserId` is a bare `Guid`, and so is every command's. This is not a compromise; it is the rule in
root `CLAUDE.md`, and Identity already models it — `GetCurrentUserQuery(Guid UserId)` and
`GetCurrentUserResult(Guid Id, string Email)` both take primitives across that seam.

The `Guid` arrives exactly as Identity's does, and the pattern is copied verbatim from
`IdentityEndpoints.GetCurrentUserAsync`:

```csharp
private const string SubjectClaimType = "sub";

if (!Guid.TryParse(principal.FindFirstValue(SubjectClaimType), out var userId))
{
    return ProblemDetailsExtensions.UnauthorizedProblem("The access token carries no usable subject.");
}
```

Never from a request body. `AddHoldingRequest` has no user field and must never gain one.

**Knock-on:** `phase-3-live-prices.md` §2.5 pins Portfolio's implementation verbatim as `h.UserId.Value`.
That line needs editing to `h.UserId` when Phase 3 lands. Recorded here so it is a known edit, not a surprise.

### 2.2 DECISION — `Ticker` is Portfolio's, and every module that needs one declares its own

> ⚠️ **Amended (§0.0).** The paragraph beginning "This is the modular-monolith answer" argued that three
> independent `Ticker` declarations are what make the extraction argument true. That argument was turned
> around and used against the module split itself: if `Ticker` means a symbol in Portfolio, a symbol in
> MarketData *and* a symbol in Alerts, the ubiquitous language never diverged, and a boundary with no
> language divergence is not a bounded context. Alerts was merged into Portfolio for exactly that reason.
> **`Ticker` is now declared twice, not three times** — once in `Portfolio.Domain`, once in MarketData —
> and those two survive because MarketData is a genuinely separate (supporting) subdomain with its own
> lifecycle. The rest of this decision stands as written.

`Ticker` is created here first, and two modules use the name: Portfolio declares it here; `phase-3` puts it in
`MarketData.Domain` *and* in `MarketData.Contracts`. That cannot be one type without either a shared domain
assembly or a strongly-typed id in `.Contracts` — and root `CLAUDE.md` forbids both (*"`Shared.Kernel` is for
types that belong to **no** module"*; *"`.Contracts` holds records of primitives only"*).

**Settled: each module declares its own `Ticker` in its own `.Domain`, and every `.Contracts` carries
`string`.** Portfolio declares the first one. The host adapter Phase 3 writes converts
`List<string>` → `IReadOnlySet<MarketData.Domain.Ticker>`, which is exactly the ten-line adapter
`phase-3` §2.5 already describes.

Two declarations across a real subdomain boundary is the modular-monolith answer. Three declarations across a
boundary that no language difference justified was the error §0.0 corrects.

**Knock-on (now moot — §2.8 is superseded, and the interface moved to `MarketData.Contracts` as `ITickersToPoll`):** `phase-3-live-prices.md` §2.5 declared it inside
`MarketData.Contracts`. That is a strongly-typed value object in a Contracts project and violates the rule.
Phase 3 must move it to `MarketData.Domain` or change it to `string`. Flagged, not fixed here.

### 2.3 DECISION — `Money` stays as it is; the complex property is mapped explicitly

This one was verified empirically, against EF Core 10.0.10 and Npgsql EF 10.0.3 on `postgres:18-alpine`,
because getting it wrong fails at **host startup**, not at first query.

`Money.Amount` and `Money.Currency` are get-only `{ get; }` auto-properties. EF's rule *"properties without
setters are not mapped by convention"* applies **inside** a complex type, so a bare
`b.ComplexProperty(h => h.AveragePrice)` finds nothing mapped, cannot bind `Money`'s constructor, and throws:

```
No suitable constructor was found for the type 'Holding.AveragePrice#Money'. The following
constructors had parameters that could not be bound to properties of the type:
    Cannot bind 'amount', 'currency' in 'Holding.AveragePrice#Money(decimal amount, string currency)'
```

Two fixes both work. **Take the explicit one:**

```csharp
builder.ComplexProperty(h => h.AveragePrice, price =>
{
    price.Property(m => m.Amount).HasColumnName("avg_price_amount").HasPrecision(18, 6);
    price.Property(m => m.Currency).HasColumnName("avg_price_currency").HasMaxLength(3).IsFixedLength();
});
```

The rejected alternative is changing `Money.Amount`/`Currency` to `{ get; init; }`, which makes the bare
one-liner work. It is rejected for three reasons: the explicit form is **required anyway** to set
`avg_price_amount`/`avg_price_currency` (Npgsql does not snake-case by convention); `init` changes how
`System.Text.Json` binds `Money`, which interacts with §2.5; and it edits a Phase 1 file with a passing test
suite to save four lines.

The cost of the explicit form, stated so nobody is surprised: **a `Money` member added later is silently
unmapped** until someone adds a `.Property()` line. There is one member-count assertion in Task 8 to catch it.

**New trap, and it belongs in `CLAUDE.md`.** EF binds a complex type's **own** constructor for
materialisation, exactly as it does an entity's. `Money(decimal, string)` calls `currency.ToUpperInvariant()`,
so **that allocation runs on every row of every `SELECT`** — confirmed from a materialiser stack trace. The
"constructor must stay guard-free" rule was written about entities; it applies identically to value objects,
and `Money` is already in breach. Accepted for Phase 2 (three columns, one row per position), recorded as a
known cost.

`efcore#31621` (complex property as a constructor parameter) is **still open**, was milestoned `11.0.0` in
February 2026, and was pushed back to `Backlog` with a `blocked` label in June 2026. It is not arriving.
So `AveragePrice` is omitted from the constructor and assigned by the factory — no parameterless constructor
is needed, which is the half of the trap `CLAUDE.md` already states correctly.

### 2.4 DECISION — layout and names

| Question | Answer | Why |
|---|---|---|
| Feature-area folder | `Holdings/` | `Portfolio/` would give `…Portfolio.Application.Portfolio.Commands.AddHolding`. Identity chose `Authentication/`, not `Identity/`, for the same reason |
| Shared success payload | `Application/HoldingSummary.cs`, at the root | Three use cases return it. `TokenPair.cs` sits in exactly that position in Identity |
| `AddHolding`'s two successes | `HoldingCreated` / `HoldingMerged`, in `Commands/AddHolding/` | Both wrap a `HoldingSummary`. The endpoint maps one to 201 and one to 200 — that distinction is the whole demo |
| `HoldingDto` | Does not exist | The spec's second name for `HoldingSummary` |
| Route prefix | `/api/holdings` | Per spec §2.4. Not `/api/portfolio/holdings` |

### 2.5 DECISION — money crosses the wire as a string, in one direction only

`src/Api/Program.cs` sets `NumberHandling = JsonNumberHandling.Strict`, and root `CLAUDE.md` requires money
serialised as strings. Phase 2 is the first phase to put money on the wire. `phase-1-implementation.md:790`
promised a `MoneyJsonConverter`; **it was never built** — the string `MoneyJsonConverter` appears exactly once
in the repository, in that sentence.

Settled, and it makes the problem smaller than §790 feared:

- **Inbound: a plain JSON number.** `AddHoldingRequest.Price` is `decimal`. `Strict` forbids a *quoted*
  number binding to `decimal`; it permits an ordinary one. The frontend's zod schema is already
  `z.coerce.number().positive()`, so a number is what arrives. No global option changes. The user typing a
  price is not the browser *computing* money, so the `CLAUDE.md` rule is intact.
- **Outbound: a `MoneyJsonConverter` in `Shared.Kernel`**, writing `{"amount":"125.000000","currency":"USD"}`.
  A converter bypasses `NumberHandling` entirely, which is exactly why it is the answer.

`Shared.Kernel` may hold it: `LayerReferenceTests.SharedKernel_ReferencesNothingButOneOfAndTheFramework`
allows anything matching `System.*`, and `System.Text.Json` is in `Microsoft.NETCore.App`. Verified against
the rule's own allow-list, which the test itself asserts with `IsFrameworkOrOneOf("System.Collections")`.

The converter writes literal lower-case `"amount"`/`"currency"`: a converter emits raw property names and the
global camelCase policy does not reach inside it.

### 2.6 DECISION — no `23505` catch; the race loser gets a 500, exactly as registration does

`phase-2-my-portfolio.md` §6 says to catch the unique violation. `phase-1-implementation.md` §5.3 removed
precisely that catch from Identity and wrote the cost down: *"the loser then hits the unique index and
surfaces as **500 rather than 409** … Reintroduce the catch only if that 500 ever actually shows up."*

**Settled: Phase 1's decision stands.** `AddHoldingCommandHandler` asks the context question with a
repository lookup and merges or creates. Two truly simultaneous POSTs both pass the lookup, one wins the
unique index and the other gets a 500. No `try`, no `catch`, no Npgsql anywhere in `.Application` — which is
what `LayerReferenceTests` requires anyway.

Three things make this the right call here rather than a copied habit:

- **The spec's own instruction is broken.** "Map it to the merge path" means retry. After a failed
  `SaveChangesAsync`, `AcceptAllChanges` is skipped and the entity is **still `Added`** — so a naive retry
  re-sends the identical `INSERT` and fails identically, forever. A correct retry must detach `ex.Entries`
  and re-run from the query. That is real subtlety bought for a millisecond-wide window.
- **`ON CONFLICT DO UPDATE` is unreachable.** It would express the merge in one atomic statement, but EF
  Core 10 cannot emit it without raw SQL: `ExecuteUpdateAsync` documents *"insertion must be done via
  `DbSet<TEntity>.Add`"*, there is no `ExecuteInsert`, `dotnet/efcore#4526` has been open on `Backlog` since
  2016, and both `npgsql/efcore.pg` upsert issues are closed as blocked. Raw SQL is banned repo-wide.
- **`Serializable` does not help.** PostgreSQL documents that *"it is possible to see unique constraint
  violations caused by conflicts with overlapping Serializable transactions even after explicitly checking
  that the key isn't present"* — unique enforcement is physical, beneath SSI. It would add `40001` to retry
  and solve nothing.

**Defence where it is cheap:** the frontend disables the Add button while the mutation is in flight
(Task 17), which removes the double-click that is the only realistic source.

Spec §5's test is rewritten accordingly — see Task 15. It asserts what actually matters: **exactly one row
survives**. Asserting "one returns a conflict" would be asserting a behaviour this plan deliberately does not
build.

### 2.7 DECISION — dispatch **after** the save commits — ⛔ MOOT (§0.0)

> **Moot, not reversed.** This decision was correct against the design it was made for, and it is kept
> verbatim below because it records why dispatch-before-save is the wrong default — an argument worth having
> written down if events ever return. But Alerts moved into Portfolio, `HoldingRemoved` has no consumer
> across a boundary, and the domain-event infrastructure is deleted. There is nothing left to dispatch, so
> there is no dispatch point to choose. Its one surviving conclusion is the one that mattered anyway:
> **Portfolio's repositories self-commit, exactly like Identity's.**

`phase-2-my-portfolio.md` §2.2 argues for dispatch-before-save so handler writes join the same transaction.
The only consumer that will ever exist — Phase 4's `HoldingRemoved` → clear the Redis cooldown key — writes
to **Redis**, which is not in the Postgres transaction. The guarantee protects a consumer that cannot use it.

**Settled: collect before the save, publish after it commits, discard if it fails.**

What this buys:

- **Portfolio repositories self-commit, exactly like Identity's.** The ⚠️ in spec §2.2 — "Portfolio's
  repositories must not commit per call" — is withdrawn. There is no new commit-point rule to state on every
  interface and quietly break later. `add-vertical-slice` step 3's question *"does the commit point already
  exist for this module?"* is answered: yes, and it is Identity's.
- **No drain loop, no clear-before-publish ordering bug, no depth-10 cap.** Nothing raised during dispatch
  can join a save that has already happened, so the recursion the spec guards against cannot occur.

What it costs, stated plainly: if the process dies between commit and publish, a cooldown key lingers until
its TTL. Nothing reads it — the holding is gone, so no alert is evaluated for that ticker — and it expires on
its own. The before-save alternative fails the other way: a rolled-back delete leaves a cooldown *cleared* for
a position you still own, and that cannot be undone, costing one un-suppressed alert.

**Why it is still two hooks and not one.** After a successful `SaveChangesAsync`, a `Deleted` entity becomes
`Detached` and vanishes from `ChangeTracker.Entries<T>()`. `HoldingRemoved` is raised by the one operation
whose entity disappears, so publishing straight from `SavedChangesAsync` would publish nothing. Events are
therefore **collected** in `SavingChangesAsync` and **published** in `SavedChangesAsync`. Full shape in Task 10.

This reverses spec §2.2 and one row of root `CLAUDE.md`'s "Where Identity is not a safe template" table.
Both edits are Task 22. (That row has since been rewritten again — §0.0 removed its driver entirely.)

### 2.8 DECISION — ~~`Portfolio.Contracts` ships two interfaces now~~ SUPERSEDED

> **Superseded.** `Portfolio.Contracts` ships **one** interface, `IUserHoldsTicker`. The poller's ticker
> list comes from Alerts, not Portfolio, because polling exists to build alert history and a ticker nobody
> has an alert on needs none; and alert evaluation owns its own subscriptions, so it already knows whom to
> notify. Both set-based reads argued for below are therefore gone. The *conclusion* that `Portfolio.Contracts`
> must carry a type still holds — rule 2 goes live either way. Original argument kept below for the record.

`phase-2-my-portfolio.md` never mentions `.Contracts`. `phase-3` needs a held-ticker read; `phase-4` needs a
holders-of-ticker read; `module-interactions.md` names the latter `IUsersHoldingTicker`. Meanwhile
`LayerReferenceTests.ContractsAssembly_ReferencesNoPersistence` currently **skips all four of its cases**,
because every `.Contracts` project is an empty shell — a rule that asserts nothing.

**Settled: two interfaces, both returning primitives, both implemented in Phase 2.** Two rather than one so a
caller that only ever needs the holders of one ticker cannot enumerate every ticker in the system.

This is not speculative work: spec §5 already schedules a test for the held-ticker read, and Task 13 implements
and tests both.

> ⚠️ **Amended (§0.0), two ways.**
>
> - **The `Shared.Kernel` reference is withdrawn.** This paragraph used to say `Portfolio.Contracts` gains a
>   `ProjectReference` to `Shared.Kernel` for `IDomainEvent`. `IDomainEvent` no longer exists, so
>   `Portfolio.Contracts` keeps no project reference at all. It holds two interfaces over primitives.
> - **`IUsersHoldingTicker`'s stated consumer changes.** It was justified as Alerts' only view of Portfolio.
>   Alerts is now inside Portfolio and can ask directly, so the interface's remaining reason to sit in
>   `.Contracts` is `ITickersHeldByAnyUser`'s: it is the seam the host adapter uses. Keep both — they are implemented and
>   tested here, and Phase 3 consumes `ITickersHeldByAnyUser` — but do not defend `IUsersHoldingTicker` on a cross-module
>   argument that no longer holds.

### 2.9 DECISION — `is_visible` ships in this migration, unused

`er-diagram.md:54` puts `is_visible` on `portfolio_holdings`. `phase-5` owns the behaviour.
`phase-3` **already assumes the column exists** — its `PollSet_IncludesHiddenHoldings` test and
`module-interactions.md:143`'s "visible holdings for user" both depend on it.

**Settled: the column ships in Phase 2's migration as `NOT NULL DEFAULT true`, with no domain method and no
API surface.** `Holding.IsVisible { get; private set; }` exists and is always `true`; there is no `Show()` or
`Hide()` until Phase 5.

This is one line now against an `ALTER TABLE` on a live Azure database mid-demo later — and it makes
`phase-5` §2.2's claim (*"defaults to true, so every existing holding keeps working with no migration data
step"*) literally true. It also avoids the `EfConstructorBindingTests` trap: adding a constructor parameter in
Phase 5 to a name-bound constructor is exactly how the whole model fails to build at startup.

`HoldingSummary` **does** carry `IsVisible`, so the wire contract is stable across Phase 5.

**One test moves.** Spec §5's `NewHolding_AppearsInPollSet_WithNoEvent` is listed as an integration test but
needs Phase 3's `ITickersHeldByAnyUser` and host adapter. Phase 2 ships and tests only the Portfolio side of that
seam (Task 13); the end-to-end assertion stays where `phase-3` §5 already has it, as
`PollSet_ReflectsHoldingsImmediately_AfterAdd`.

---

## 3. Global constraints

Every task's requirements implicitly include this section. Values are copied from the files that hold them,
not from any plan document.

- **TFM `net10.0`, `LangVersion 14.0`, `Nullable enable`, `TreatWarningsAsErrors true`.** From
  `Directory.Build.props`. A warning is a build failure.
- **Package versions live in `Directory.Packages.props` and nowhere else.** Phase 2 adds **no**
  `PackageVersion` entry. Every package it needs is already there.
- **Namespace prefix `StockPortfolio.Modules.Portfolio.<Layer>`.** Load-bearing:
  `PortfolioDbContext.OnModelCreating` filters `ApplyConfigurationsFromAssembly` on it, and a wrong prefix
  silently skips every configuration.
- **Accessibility follows the onion.** `.Domain` / `.Application` / `.Api` public; `.Infrastructure`
  `internal` except `PortfolioModule`.
- **Reference rules, compiler- and test-enforced.** `.Infrastructure` never references ASP.NET Core;
  `.Api` never references EF Core or its own `.Infrastructure`; `.Contracts` never references EF Core or
  Npgsql; a module reaches another module only through its `.Contracts`.
- **One `/// <summary>…</summary>` per public type and member, one line.** No `<remarks>`, no `<param>`.
  `GenerateDocumentationFile` is on repo-wide.
- **No `ConfigureAwait(false)`, no `IUnitOfWork`, no `[GenerateOneOf]`, no `_ =>` in a `.Match`.**
- **Every `.Match` lambda parameter is named.** `merged =>`, not `_ =>`.
- **Every endpoint declares every status it can emit**, verified against a real response, read back from
  `/openapi/v1.json` — not from the source.
- **Entity constructors stay guard-free.** EF binds them by name for materialisation and runs whatever is
  inside on every row of every `SELECT`. Guards belong in the static factory.
- **`Maximum Pool Size=2`** on every production connection string. Four roles are four Npgsql pools.
- **Test method names are `Method_Scenario_Expectation`.** `CA1707` is suppressed in
  `tests/Directory.Build.props` for exactly this.
- **Quote passing *and* skipped test counts.** A rising skip count means a rule stopped asserting.

### The merge arithmetic, settled

These three values are the repo owner's call, made before the tests that encode them:

```csharp
// Rounding: 6 decimal places, banker's rounding, applied on store.
//   1 @ $0.333333 + 2 @ $0.666667 -> $0.555556, stored exactly as the column holds it.
//   numeric(18,6) would round on INSERT anyway; rounding here keeps the in-memory value
//   and the persisted value identical, so a re-read never changes the number.
AveragePrice = new Money(Math.Round(weighted, 6, MidpointRounding.ToEven), AveragePrice.Currency);

// Zero price: rejected. A $0 buy drags the average toward zero and reads as a bug on the dashboard.
// Dust quantity: rejected below one unit of the column's precision. 0.0000001 rounds to 0 in
//   numeric(18,6), and the next Merge would then divide by zero.
private const decimal MinimumQuantity = 0.000001m;
```

---

## 4. File map

`✚` created · `✎` modified · `⛔` withdrawn by §0.0 · everything else untouched.

```
src/
  Shared.Kernel/
 ⛔  DomainEvents/IDomainEvent.cs               withdrawn; no raiser once Alerts moved into Portfolio
 ⛔  DomainEvents/IDomainEventHandler.cs        withdrawn
 ⛔  DomainEvents/IDomainEventPublisher.cs      withdrawn
 ✚  MoneyJsonConverter.cs                      money out as a string (§2.5)

  Shared.Api/
 ✎  ProblemDetailsExtensions.cs                + NotFoundProblem; three routes need 404

  Modules/Portfolio/
    …Portfolio.Contracts/
 ⛔    *.csproj                                 the Shared.Kernel reference was only for IDomainEvent
 ⛔    HoldingRemoved.cs                        withdrawn; removal is a method call inside one module
 ✚    ITickersHeldByAnyUser.cs                 Task<List<string>>  — host adapter, Phase 3
 ✚    IUsersHoldingTicker.cs                   Task<List<Guid>>    — alert evaluation, Phase 4

    …Portfolio.Domain/
 ✚    HoldingId.cs                             UUIDv7, six lines, copied from UserId.cs
 ✚    Ticker.cs                                value object; uppercase; ^[A-Z]{1,5}$
 ✚    Holding.cs                               the aggregate: Create / Merge / Correct / Remove

    …Portfolio.Application/
 ✚    HoldingSummary.cs                        the shared success payload (TokenPair's position)
 ✚    Abstractions/IHoldingRepository.cs       commit point stated in the doc comment
 ✚    Holdings/Commands/AddHolding/            command · handler · HoldingCreated · HoldingMerged · UnknownTicker
 ✚    Holdings/Commands/UpdateHolding/         command · handler
 ✚    Holdings/Commands/RemoveHolding/         command · handler
 ✚    Holdings/Queries/GetHoldings/            query · handler

    …Portfolio.Infrastructure/
 ✚    AssemblyInfo.cs                          InternalsVisibleTo the new unit-test project
 ✚    PortfolioModule.cs                       the module's entire public surface
 ✚    DependencyInjection.cs                   handler registrations, closed generics spelled out
 ✚    Persistence/PortfolioDbContext.cs        schema `portfolio`, its own history table
 ✚    Persistence/PortfolioDbContextFactory.cs design-time; dotnet ef must not need local config
 ✚    Persistence/Configurations/HoldingConfiguration.cs
 ✚    Persistence/Converters/HoldingIdConverter.cs
 ✚    Persistence/Converters/TickerConverter.cs
 ✚    Persistence/HoldingRepository.cs         self-commits, like Identity's (§2.7)
 ✚    Persistence/HoldingQueries.cs            ITickersHeldByAnyUser + IUsersHoldingTicker, AsNoTracking
 ⛔    Persistence/DispatchDomainEventsInterceptor.cs   withdrawn with Task 10
 ⛔    Persistence/DomainEventPublisher.cs              withdrawn with Task 10
 ✚    Persistence/Migrations/                  generated by dotnet ef

    …Portfolio.Api/
 ✚    PortfolioEndpoints.cs                    AddPortfolioApi + MapPortfolioEndpoints
 ✚    Requests/AddHoldingRequest.cs
 ✚    Requests/UpdateHoldingRequest.cs
 ✚    Validators/AddHoldingRequestValidator.cs
 ✚    Validators/UpdateHoldingRequestValidator.cs

  Api/
 ✎  Program.cs                                 3 lines, ABOVE DecorateHandlers()
 ✎  appsettings.Development.json               + ConnectionStrings:Portfolio
 ✎  StockPortfolio.Api.http                    + the four holdings calls

  Migrator/
 ✎  Program.cs                                 + AddPortfolioModule — the P0 blocker

  Web/src/
 ✚  portfolio/holdingsApi.ts                   fetchers + holdingKeys, mirroring auth/authApi.ts
 ✚  portfolio/useHoldingMutations.ts           the three optimistic mutations
 ✚  routes/_authenticated/portfolio.tsx        table + add form + Invested
 ✚  components/Table.tsx                       new; there is no Phase 1 table
 ✚  components/ConfirmDialog.tsx               new; focus trap, Escape, aria-modal
 ✎  components/AppShell.tsx                    + the Portfolio nav entry
 ✎  routeTree.gen.ts                           generated, but committed

tests/
 ✚ StockPortfolio.Modules.Portfolio.UnitTests/ new project; slnx entry; GlobalUsings
 ✎ StockPortfolio.Architecture.Tests/ModuleBoundaryTests.cs     two hard-coded lists
 ✎ StockPortfolio.Api.IntegrationTests/Infrastructure/ApiFixture.cs
 ✎ StockPortfolio.Api.IntegrationTests/Infrastructure/ModuleDbContextInterceptors.cs
 ✎ StockPortfolio.Api.IntegrationTests/MigrationTests.cs
 ✎ StockPortfolio.Api.IntegrationTests/EndpointMetadataTests.cs
 ✚ StockPortfolio.Api.IntegrationTests/HoldingsTests.cs
 ✚ StockPortfolio.Shared.Kernel.UnitTests/MoneyJsonConverterTests.cs
 ✚ src/Web/tests/portfolio.test.tsx

StockPortfolio.slnx                            ✎ + the new test project
CLAUDE.md                                      ✎ four corrections (Task 22)
docs/plan/phase-2-my-portfolio.md              ✎ the eleven corrections in §0
README.md                                      ✎ the weighted-average rule
```

**No `.csproj` is created for `src/`.** All five Portfolio shells exist and are already correctly
referenced — `Domain → Shared.Kernel`, `Application → Domain + Contracts`, `Infrastructure → Application` +
EF + Npgsql, `Api → Application + Shared.Api` + FluentValidation + `Microsoft.AspNetCore.App`. `src/Api` and
`src/Migrator` already reference all of them. The only new project in the repo is the unit-test one.

**`db/init/01-roles.sql` needs nothing.** The `portfolio` schema, the `portfolio_svc` role, its grants and
`ALTER DEFAULT PRIVILEGES` were all created in Phase 1, precisely so `PortfolioRole_CannotReadIdentitySchema`
could be a real test from day one. Verify, do not add.

---

## 5. Tasks

Build inward-out; each task compiles and its tests pass before the next begins.

---

### Task 1: Domain-event types in `Shared.Kernel` — ⛔ WITHDRAWN

> **Do not build this task.** Withdrawn by §0.0. It exists only because Alerts was a separate module and
> could not be told about a removed holding by a method call. With Alerts inside Portfolio, `HoldingRemoved`
> has no consumer across a boundary, so the three interfaces below would once again be an abstraction with no
> raiser — which is the precise reason Phase 1 deleted `IDomainEvent` in the first place
> (`phase-1-implementation.md` §5.2). `src/Shared.Kernel/DomainEvents/` does not exist and must not be
> created. Task numbering is preserved so execution against these numbers does not shift.
>
> The original task is left below, unedited, as the record of what was planned and rejected.

Phase 1 wrote `IDomainEvent`, found nothing raised it, and deleted it. `HoldingRemoved` is the first real one,
so it comes back — with the two collaborators the single consumer needs, and nothing else. **No
`AggregateRoot<TId>`.**

**Files:**
- Create: `src/Shared.Kernel/DomainEvents/IDomainEvent.cs`
- Create: `src/Shared.Kernel/DomainEvents/IDomainEventHandler.cs`
- Create: `src/Shared.Kernel/DomainEvents/IDomainEventPublisher.cs`

**Interfaces:**
- Produces: `IDomainEvent` (marker); `IDomainEventHandler<TEvent> where TEvent : IDomainEvent` with
  `Task Handle(TEvent domainEvent, CancellationToken ct)`; `IDomainEventPublisher` with
  `Task PublishAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken ct)`.
- Consumed by: Task 5 (`HoldingRemoved`), Task 10 (interceptor + publisher), Phase 4 (Alerts implements the handler).

- [ ] **Step 1: Write the three files**

```csharp
// src/Shared.Kernel/DomainEvents/IDomainEvent.cs
namespace StockPortfolio.Shared.Kernel.DomainEvents;

/// <summary>Something that happened in a domain, which another module may care about.</summary>
public interface IDomainEvent;
```

```csharp
// src/Shared.Kernel/DomainEvents/IDomainEventHandler.cs
namespace StockPortfolio.Shared.Kernel.DomainEvents;

/// <summary>Reacts to one kind of domain event, in whichever module cares.</summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>Handles the event. Runs after the originating save has committed.</summary>
    Task Handle(TEvent domainEvent, CancellationToken ct);
}
```

```csharp
// src/Shared.Kernel/DomainEvents/IDomainEventPublisher.cs
namespace StockPortfolio.Shared.Kernel.DomainEvents;

/// <summary>Delivers raised events to their handlers.</summary>
public interface IDomainEventPublisher
{
    /// <summary>Delivers every event to every handler registered for its concrete type.</summary>
    Task PublishAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken ct);
}
```

`IDomainEventHandler<in TEvent>` is contravariant so a handler of a base event type would still resolve;
today there is one event and one handler, and the variance costs nothing.

- [ ] **Step 2: Confirm the kernel is still framework-free**

Run: `dotnet test tests/StockPortfolio.Architecture.Tests --filter SharedKernel_ReferencesNothingButOneOfAndTheFramework`
Expected: PASS. Three interfaces over `Task` and `IReadOnlyCollection` add only `System.*` references, which
the rule's `IsFrameworkOrOneOf` allow-list permits.

- [ ] **Step 3: Commit**

```bash
git add src/Shared.Kernel/DomainEvents && git commit -m "Bring back IDomainEvent, for something that actually raises one"
```

---

### Task 2: `MoneyJsonConverter`

`NumberHandling.Strict` plus "money is serialised as strings" is only a contradiction if money is a bare
`decimal` on the wire. A converter sidesteps `NumberHandling` entirely (§2.5).

**Files:**
- Create: `src/Shared.Kernel/MoneyJsonConverter.cs`
- Create: `tests/StockPortfolio.Shared.Kernel.UnitTests/MoneyJsonConverterTests.cs`
- Modify: `src/Api/Program.cs` — one line inside `ConfigureHttpJsonOptions`

**Interfaces:**
- Produces: `MoneyJsonConverter : JsonConverter<Money>`, emitting `{"amount":"125.000000","currency":"USD"}`.
- Consumed by: Task 11 (`HoldingSummary` carries `Money`), Phase 3 (every P&L figure).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/StockPortfolio.Shared.Kernel.UnitTests/MoneyJsonConverterTests.cs
using System.Text.Json;
using Shouldly;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Tests;

public sealed class MoneyJsonConverterTests
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { Converters = { new MoneyJsonConverter() } };

    [Fact]
    public void Write_EmitsAmountAsString_NotAsNumber() =>
        JsonSerializer.Serialize(Money.Usd(125.5m), Options)
            .ShouldBe("""{"amount":"125.5","currency":"USD"}""");

    [Fact]
    public void Write_PreservesTrailingZeroes_SoSixDecimalsSurvive() =>
        JsonSerializer.Serialize(Money.Usd(0.555556m), Options)
            .ShouldBe("""{"amount":"0.555556","currency":"USD"}""");

    [Fact]
    public void RoundTrip_PreservesAmountAndCurrency()
    {
        var original = Money.Usd(1234.567891m);

        JsonSerializer.Deserialize<Money>(JsonSerializer.Serialize(original, Options), Options)
            .ShouldBe(original);
    }

    // The reason the converter exists: Strict rejects a quoted number for a bare decimal.
    [Fact]
    public void Read_AcceptsTheStringForm_UnderStrictNumberHandling()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
            Converters = { new MoneyJsonConverter() },
        };

        JsonSerializer.Deserialize<Money>("""{"amount":"7.25","currency":"usd"}""", options)
            .ShouldBe(Money.Usd(7.25m));
    }

    [Fact]
    public void Read_MissingCurrency_Throws() =>
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<Money>("""{"amount":"1"}""", Options));
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test tests/StockPortfolio.Shared.Kernel.UnitTests --filter MoneyJsonConverterTests`
Expected: FAIL — `MoneyJsonConverter` does not exist (CS0246).

- [ ] **Step 3: Write the converter**

```csharp
// src/Shared.Kernel/MoneyJsonConverter.cs
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StockPortfolio.Shared.Kernel;

/// <summary>Serialises Money with the amount as a string, so no consumer parses it as a float.</summary>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    private const string AmountName = "amount";
    private const string CurrencyName = "currency";

    /// <inheritdoc/>
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Money must be an object with 'amount' and 'currency'.");
        }

        decimal? amount = null;
        string? currency = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();

            if (string.Equals(name, AmountName, StringComparison.Ordinal))
            {
                // The string form is the point: a quoted number is what Strict would otherwise reject.
                amount = reader.TokenType == JsonTokenType.String
                    ? decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture)
                    : reader.GetDecimal();
            }
            else if (string.Equals(name, CurrencyName, StringComparison.Ordinal))
            {
                currency = reader.GetString();
            }
        }

        if (amount is null || string.IsNullOrWhiteSpace(currency))
        {
            throw new JsonException("Money requires both 'amount' and 'currency'.");
        }

        return new Money(amount.Value, currency);
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // Written literally: a converter emits raw names, so the global camelCase policy never sees these.
        writer.WriteStartObject();
        writer.WriteString(AmountName, value.Amount.ToString(CultureInfo.InvariantCulture));
        writer.WriteString(CurrencyName, value.Currency);
        writer.WriteEndObject();
    }
}
```

- [ ] **Step 4: Register it on the host**

In `src/Api/Program.cs`, inside `ConfigureHttpJsonOptions`, directly below the existing
`options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());`:

```csharp
        // Money is decimal server-side and a string on the wire; a converter bypasses NumberHandling.Strict.
        options.SerializerOptions.Converters.Add(new MoneyJsonConverter());
```

and add `using StockPortfolio.Shared.Kernel;` to the file's usings.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/StockPortfolio.Shared.Kernel.UnitTests`
Expected: PASS — 14 existing `MoneyTests` plus 5 new.

- [ ] **Step 6: Commit**

```bash
git add src/Shared.Kernel/MoneyJsonConverter.cs tests/StockPortfolio.Shared.Kernel.UnitTests/MoneyJsonConverterTests.cs src/Api/Program.cs
git commit -m "Money leaves as a string, so nothing downstream parses it as a float"
```

---

### Task 3: The `Portfolio.UnitTests` project

`add-vertical-slice` §6 says *"add to the existing projects — do not create new ones."* That rule is about
adding a slice to a built module. Phase 2 builds a module, and `phase-2-my-portfolio.md` §5 and
`phase-3-live-prices.md` §5 both name `Portfolio.UnitTests` as the home for fourteen and seven tests
respectively. It is created once, here.

**Files:**
- Create: `tests/StockPortfolio.Modules.Portfolio.UnitTests/StockPortfolio.Modules.Portfolio.UnitTests.csproj`
- Create: `tests/StockPortfolio.Modules.Portfolio.UnitTests/GlobalUsings.cs`
- Create: `src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/AssemblyInfo.cs`
- Modify: `StockPortfolio.slnx`

**Interfaces:**
- Produces: an xUnit v3 assembly in namespace `StockPortfolio.Tests`, with `internal` visibility into
  `Portfolio.Infrastructure` so Task 8 can build the EF model.

- [ ] **Step 1: Copy Identity's test csproj shape**

```xml
<!-- tests/StockPortfolio.Modules.Portfolio.UnitTests/StockPortfolio.Modules.Portfolio.UnitTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Shouldly" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Modules\Portfolio\StockPortfolio.Modules.Portfolio.Infrastructure\StockPortfolio.Modules.Portfolio.Infrastructure.csproj" />
    <ProjectReference Include="..\..\src\Modules\Portfolio\StockPortfolio.Modules.Portfolio.Api\StockPortfolio.Modules.Portfolio.Api.csproj" />
  </ItemGroup>

</Project>
```

No `<PackageVersion>` anywhere — CPM supplies every version, and `tests/Directory.Build.props` supplies
`IsPackable`, `IsTestProject`, `GenerateDocumentationFile=false` and the `CA1707` suppression. The `.Api`
reference is what brings FluentValidation in transitively for the validator tests in Task 16, exactly as
`Identity.UnitTests` gets it.

```csharp
// tests/StockPortfolio.Modules.Portfolio.UnitTests/GlobalUsings.cs
global using Xunit;
```

- [ ] **Step 2: Open Infrastructure to the test assembly**

```csharp
// src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/AssemblyInfo.cs
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("StockPortfolio.Modules.Portfolio.UnitTests")]
```

Its own file, never a rider on a type file — deleting an unrelated type has twice taken an assembly-level
attribute with it and broken the build.

- [ ] **Step 3: Add it to the solution**

In `StockPortfolio.slnx`, inside the `/tests/` folder element, beside the other four test projects:

```xml
    <Project Path="tests/StockPortfolio.Modules.Portfolio.UnitTests/StockPortfolio.Modules.Portfolio.UnitTests.csproj" />
```

- [ ] **Step 4: Verify it builds and is discovered**

Run: `dotnet build && dotnet test tests/StockPortfolio.Modules.Portfolio.UnitTests`
Expected: build clean, `0` tests run, exit 0. An empty test project is legal and this proves the wiring
before any test depends on it.

- [ ] **Step 5: Commit**

```bash
git add tests/StockPortfolio.Modules.Portfolio.UnitTests src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/AssemblyInfo.cs StockPortfolio.slnx
git commit -m "A test project for the module about to exist"
```

---

### Task 4: `HoldingId` and `Ticker`

**Files:**
- Create: `src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Domain/HoldingId.cs`
- Create: `src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Domain/Ticker.cs`
- Create: `tests/StockPortfolio.Modules.Portfolio.UnitTests/TickerTests.cs`

**Interfaces:**
- Produces: `readonly record struct HoldingId(Guid Value)` with `static HoldingId New()`;
  `readonly record struct Ticker(string Value)` with `static OneOf<Ticker, InvalidInput> Create(string?)`
  and `const int MaxLength = 5`.
- Consumed by: Task 5 (`Holding`), Task 7 (converters), Task 12 (handlers).

⚠️ This is the first type in `Portfolio.Domain`. The moment it compiles, four architecture rules stop
skipping for that assembly and `EmptyShells_AreExactlyThePhasesNotYetBuilt` goes red — that is the deliberate
gate, and Task 18 walks it. Expect a red architecture suite from here until then.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/StockPortfolio.Modules.Portfolio.UnitTests/TickerTests.cs
using Shouldly;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Tests;

public sealed class TickerTests
{
    [Theory]
    [InlineData("aapl", "AAPL")]
    [InlineData("  msft  ", "MSFT")]
    [InlineData("F", "F")]
    public void Create_Normalises_ToTrimmedUppercase(string input, string expected) =>
        Ticker.Create(input).AsT0.Value.ShouldBe(expected);

    [Theory]
    [InlineData("TOOLONG")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("AA PL")]
    [InlineData("BRK.B")]
    [InlineData("AAPL1")]
    [InlineData("'; DROP TABLE portfolio.holdings; --")]
    public void Create_RejectsAnythingOutsideTheShape(string? input) =>
        Ticker.Create(input).AsT1.Field.ShouldBe("ticker");

    [Fact]
    public void Equality_IsOrdinal_OnTheNormalisedValue() =>
        Ticker.Create("aapl").AsT0.ShouldBe(Ticker.Create("AAPL").AsT0);
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/StockPortfolio.Modules.Portfolio.UnitTests --filter TickerTests`
Expected: FAIL with CS0246 — `Ticker` does not exist.

- [ ] **Step 3: Write both types**

```csharp
// src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Domain/HoldingId.cs
namespace StockPortfolio.Modules.Portfolio.Domain;

/// <summary>The identity of a holding.</summary>
public readonly record struct HoldingId(Guid Value)
{
    /// <summary>Creates a fresh, index-local id. UUIDv7 in the domain, because Npgsql's sequential.</summary>
    public static HoldingId New() => new(Guid.CreateVersion7());

    /// <inheritdoc/>
    public override string ToString() => Value.ToString();
}
```

Six lines, copied from `UserId.cs` for the reason stated there: Npgsql's sequential-GUID generator selects on
`property.ClrType`, which is `HoldingId` and not `Guid`, so it would never fire. Generate v7 in the domain and
map `ValueGeneratedNever()`.

```csharp
// src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Domain/Ticker.cs
using System.Text.RegularExpressions;
using OneOf;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Domain;

/// <summary>A stock symbol, upper-cased and shape-checked. Portfolio's own; other modules declare theirs.</summary>
public readonly partial record struct Ticker(string Value)
{
    /// <summary>The longest symbol the shape allows.</summary>
    public const int MaxLength = 5;

    /// <summary>Creates a ticker, trimming and upper-casing first.</summary>
    public static OneOf<Ticker, InvalidInput> Create(string? candidate)
    {
        var normalised = (candidate ?? string.Empty).Trim().ToUpperInvariant();

        return Shape().IsMatch(normalised)
            ? new Ticker(normalised)
            : new InvalidInput("ticker", $"A ticker is 1 to {MaxLength} letters, A to Z.");
    }

    /// <inheritdoc/>
    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z]{1,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
```

Three things that are decisions, not style:

- **The constructor is public**, because `TickerConverter` lives in `.Infrastructure` — a different assembly —
  and must call `new Ticker(value)` to materialise. `Create` is the validating way in, exactly as
  `UserId(Guid)` is public while `UserId.New()` is the intended door.
- **`[GeneratedRegex]`, not `Regex.IsMatch`.** `TreatWarningsAsErrors` promotes `SYSLIB1045` to an error.
  This is why the struct is `partial`.
- **Canonicalisation lives in `Ticker.Create`, not on the entity.** `User.NormaliseEmail` is a static on the
  entity only because email is a bare `string` with nowhere else to live. `Ticker` is a value object and owns
  its own canonical form — the row in `CLAUDE.md`'s "Where Identity is not a safe template" table.

⚠️ **Struct hazard, accepted and worth knowing:** every struct has an implicit parameterless constructor, so
`default(Ticker).Value` is `null`. No code path produces one — `Holding` only ever assigns from `Create` or
from the converter — but a future `Ticker` field left unassigned would be a null-reference away from a
`NullReferenceException` inside EF's materialiser.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/StockPortfolio.Modules.Portfolio.UnitTests --filter TickerTests`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Domain tests/StockPortfolio.Modules.Portfolio.UnitTests/TickerTests.cs
git commit -m "Portfolio's first two types: an id and a ticker that owns its canonical form"
```

---

### Task 5: `Portfolio.Contracts` — one boolean (⛔ the event and both set-based reads are WITHDRAWN)

> **Superseded.** This task originally shipped `ITickersHeldByAnyUser` and `IUsersHoldingTicker`. Neither is
> needed: the poller polls tickers with an *active alert*, which Alerts owns, and alert evaluation therefore
> already knows whom to notify. What survives is a single validation seam:
>
> ```csharp
> // Portfolio.Contracts/IUserHoldsTicker.cs
> public interface IUserHoldsTicker
> {
>     /// <summary>Whether this user has a position in this ticker. Used to reject an alert on something you do not own.</summary>
>     Task<bool> HoldsAsync(Guid userId, string ticker, CancellationToken ct);
> }
> ```
>
> Everything below about the `.csproj` references and rule 2 going live still applies — `Portfolio.Contracts`
> carries a type either way. The `Shared.Kernel` reference is **not** needed, since there is no `IDomainEvent`.

> **Partly withdrawn by §0.0.** `HoldingRemoved.cs` is **not** created, `Portfolio.Contracts` gains **no**
> `ProjectReference` to `Shared.Kernel`, and `Portfolio.Domain` gains **no** reference to
> `Portfolio.Contracts` — all three existed only to carry a cross-module event that no longer crosses
> anything. `ITickersHeldByAnyUser.cs` and `IUsersHoldingTicker.cs` still ship, unchanged, and Step 3's check (rule 2 going
> live for the first time on any module) still applies. Skip Step 1 entirely and build only the two
> interfaces in Step 2. Task numbering is preserved.
>
> The withdrawn material is left below, unedited, as the record of what was planned and rejected.

Written before `Holding`, because `Holding` raises `HoldingRemoved` and therefore references this project.

**Files:**
- ⛔ Modify: `src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Contracts/StockPortfolio.Modules.Portfolio.Contracts.csproj`
- ⛔ Create: `.../Portfolio.Contracts/HoldingRemoved.cs`
- Create: `.../Portfolio.Contracts/ITickersHeldByAnyUser.cs`
- Create: `.../Portfolio.Contracts/IUsersHoldingTicker.cs`
- ⛔ Modify: `src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Domain/StockPortfolio.Modules.Portfolio.Domain.csproj`

**Interfaces:**
- Produces: ⛔ `HoldingRemoved(Guid UserId, string Ticker) : IDomainEvent`;
  `ITickersHeldByAnyUser.GetAsync(CancellationToken) → Task<List<string>>`;
  `IUsersHoldingTicker.GetAsync(string ticker, CancellationToken) → Task<List<Guid>>`.
- Consumed by: Task 14 (implementations), Phase 3 (host adapter), Phase 4 (alert evaluation, inside Portfolio).

- [ ] ⛔ **Step 1: Give Contracts and Domain the references they need — WITHDRAWN, skip it**

`Portfolio.Contracts.csproj` — currently `<Project Sdk="Microsoft.NET.Sdk"></Project>` with no ItemGroup at all:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <!-- IDomainEvent only. Rule 2 forbids EF Core and Npgsql here; Shared.Kernel is neither. -->
    <ProjectReference Include="..\..\..\Shared.Kernel\StockPortfolio.Shared.Kernel.csproj" />
  </ItemGroup>

</Project>
```

`Portfolio.Domain.csproj` gains one line, so the entity can raise a contracts-shaped event:

```xml
    <ProjectReference Include="..\StockPortfolio.Modules.Portfolio.Contracts\StockPortfolio.Modules.Portfolio.Contracts.csproj" />
```

⚠️ This edge — `<M>.Domain → <M>.Contracts` — is not in `phase-1-implementation.md` §4.1's reference table,
which gives `.Domain` only `Shared.Kernel`. It is legal (rule 1 only polices reaching into *another* module,
and rule 2 only forbids persistence in Contracts) and it is necessary: the event must be a record of
primitives so Alerts can consume it without seeing `Portfolio.Domain`. §4.1 is amended in Task 23.
**Withdrawn:** with no event, neither reference is needed and §4.1 needs no amendment.
`Portfolio.Contracts` stays reference-free.

- [ ] **Step 2: Write the two files** *(the third, `HoldingRemoved.cs`, is withdrawn)*

```csharp
// ⛔ WITHDRAWN — src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Contracts/HoldingRemoved.cs
using StockPortfolio.Shared.Kernel.DomainEvents;

namespace StockPortfolio.Modules.Portfolio.Contracts;

/// <summary>A user closed a position. Alerts clears that user's cooldown keys for the ticker.</summary>
public sealed record HoldingRemoved(Guid UserId, string Ticker) : IDomainEvent;
```

Primitives, deliberately: `Guid` not `UserId`, `string` not `Ticker`. This record is the entire surface Alerts
ever sees of Portfolio.

⚠️ **Phase 4 note, and it survives the withdrawal in changed form:** the cooldown key is
`alerts:cooldown:{userId}:{ticker}:{direction}` with `direction ∈ Drawdown | RunUp`. Removing a holding must
still clear **both** keys, because there is no single direction to clear. Phase 4 does it from
`RemoveHoldingCommandHandler` — a direct call inside Portfolio, not an event handler.

```csharp
// src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Contracts/ITickersHeldByAnyUser.cs
namespace StockPortfolio.Modules.Portfolio.Contracts;

/// <summary>Every ticker anyone holds, read live each poll cycle rather than cached in MarketData.</summary>
public interface ITickersHeldByAnyUser
{
    /// <summary>Returns the distinct tickers across all users, including hidden holdings.</summary>
    Task<List<string>> GetAsync(CancellationToken ct);
}
```

```csharp
// src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Contracts/IUsersHoldingTicker.cs
namespace StockPortfolio.Modules.Portfolio.Contracts;

/// <summary>Who holds a given ticker. Alerts asks this to decide whom a breach concerns.</summary>
public interface IUsersHoldingTicker
{
    /// <summary>Returns the distinct user ids holding the ticker, including hidden holdings.</summary>
    Task<List<Guid>> GetAsync(string ticker, CancellationToken ct);
}
```

Two interfaces rather than one so a caller that only ever needs holders cannot enumerate every ticker in the
system (§2.8). Both say "including hidden holdings" in the doc comment because Phase 5's visibility flag is a
**display** filter and must not narrow either read — `phase-3` has a test named for exactly that.

- [ ] **Step 3: Verify the boundary rule now runs instead of skipping**

Run: `dotnet build && dotnet test tests/StockPortfolio.Architecture.Tests --filter ContractsAssembly_ReferencesNoPersistence`
Expected: `Portfolio.Contracts` **passes** rather than skips; the other three still skip. This is the rule
going live — before this task it asserted nothing at all, on any module.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Contracts
git commit -m "Contracts: the two reads phases 3 and 4 need"
```

---

### Task 6: The `Holding` aggregate

> ⚠️ **Amended by §0.0.** Everything here still builds **except the event surface**. `Holding` has no
> `_domainEvents` field, no `DomainEvents` projection, no `ClearDomainEvents()` and no `Remove()` that records
> an event — removal is `HoldingRepository.RemoveAsync` and nothing more. Drop
> `Remove_RaisesHoldingRemoved_Once`, `Create_And_Merge_RaiseNoEvents` and `ClearDomainEvents_EmptiesTheList`
> from `HoldingTests`; the merge, correction and validation tests are unaffected. Phase 4's cooldown clearing
> is a call from `RemoveHoldingCommandHandler`, not an event.

The centre of the phase. Everything decided in §2.1, §2.3 and §3 lands here.

**Files:**
- Create: `src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Domain/Holding.cs`
- Create: `tests/StockPortfolio.Modules.Portfolio.UnitTests/HoldingTests.cs`

**Interfaces:**
- Produces: `Holding` with `Id, UserId, Ticker, Quantity, AveragePrice, IsVisible, CreatedAt, UpdatedAt,
  DomainEvents`; `static OneOf<Holding, InvalidInput> Create(Guid, Ticker, decimal, Money, TimeProvider)`;
  `OneOf<Success, InvalidInput> Merge(decimal, Money, TimeProvider)`;
  `OneOf<Success, InvalidInput> Correct(decimal, Money, TimeProvider)`; `void Remove()`;
  `void ClearDomainEvents()`.
- Consumed by: Task 7 (mapping), Task 9 (repository), Task 12 (handlers), Task 10 (the interceptor drains
  `DomainEvents`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/StockPortfolio.Modules.Portfolio.UnitTests/HoldingTests.cs
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Tests;

public sealed class HoldingTests
{
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Ticker Aapl = Ticker.Create("AAPL").AsT0;

    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

    private static Holding At(decimal quantity, decimal price) =>
        Holding.Create(User, Aapl, quantity, Money.Usd(price), Clock).AsT0;

    // The canonical case from Initial.md:104.
    [Fact]
    public void Merge_TenAtHundredPlusTenAtOneFifty_GivesTwentyAtOneTwentyFive()
    {
        var holding = At(10m, 100m);

        holding.Merge(10m, Money.Usd(150m), Clock).IsT0.ShouldBeTrue();

        holding.Quantity.ShouldBe(20m);
        holding.AveragePrice.ShouldBe(Money.Usd(125m));
    }

    [Fact]
    public void Merge_ThreeSuccessivePurchases_WeightsCorrectly()
    {
        var holding = At(10m, 100m);

        holding.Merge(5m, Money.Usd(200m), Clock);
        holding.Merge(5m, Money.Usd(50m), Clock);

        holding.Quantity.ShouldBe(20m);
        holding.AveragePrice.ShouldBe(Money.Usd(112.50m));
    }

    // Encodes the rounding decision: 6dp, banker's, on store.
    [Fact]
    public void Merge_RoundsToSixDecimals_ToEven()
    {
        var holding = At(1m, 0.333333m);

        holding.Merge(2m, Money.Usd(0.666667m), Clock);

        holding.AveragePrice.Amount.ShouldBe(0.555556m);
        decimal.Round(holding.AveragePrice.Amount, 6).ShouldBe(holding.AveragePrice.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0000001)]
    public void Merge_QuantityBelowOneMicroUnit_ReturnsInvalidInput(decimal quantity) =>
        At(10m, 100m).Merge(quantity, Money.Usd(150m), Clock).AsT1.Field.ShouldBe("quantity");

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Merge_NonPositivePrice_ReturnsInvalidInput(decimal price) =>
        At(10m, 100m).Merge(5m, Money.Usd(price), Clock).AsT1.Field.ShouldBe("price");

    // Money.Add THROWS on a currency mismatch, so Merge must compare before it does any arithmetic.
    [Fact]
    public void Merge_DifferentCurrency_ReturnsInvalidInput_RatherThanThrowing() =>
        At(10m, 100m).Merge(5m, new Money(150m, "EUR"), Clock).AsT1.Field.ShouldBe("price");

    [Fact]
    public void Merge_LeavesQuantityUntouched_WhenItRejects()
    {
        var holding = At(10m, 100m);

        holding.Merge(0m, Money.Usd(150m), Clock);

        holding.Quantity.ShouldBe(10m);
        holding.AveragePrice.ShouldBe(Money.Usd(100m));
    }

    [Fact]
    public void Correct_ReplacesRatherThanAverages()
    {
        var holding = At(10m, 100m);
        holding.Merge(10m, Money.Usd(150m), Clock);      // now 20 @ $125

        holding.Correct(10m, Money.Usd(100m), Clock).IsT0.ShouldBeTrue();

        holding.Quantity.ShouldBe(10m);
        holding.AveragePrice.ShouldBe(Money.Usd(100m));
    }

    [Fact]
    public void Remove_RaisesHoldingRemoved_Once()
    {
        var holding = At(10m, 100m);

        holding.Remove();

        holding.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<HoldingRemoved>()
            .ShouldSatisfyAllConditions(
                raised => raised.UserId.ShouldBe(User),
                raised => raised.Ticker.ShouldBe("AAPL"));
    }

    // The held-ticker list is read live from Portfolio each cycle, so nothing needs telling about an addition.
    [Fact]
    public void Create_And_Merge_RaiseNoEvents()
    {
        var holding = At(10m, 100m);
        holding.Merge(10m, Money.Usd(150m), Clock);
        holding.Correct(5m, Money.Usd(90m), Clock);

        holding.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Create_NewHolding_IsVisible() => At(10m, 100m).IsVisible.ShouldBeTrue();

    [Fact]
    public void Merge_StampsUpdatedAt_LeavingCreatedAtAlone()
    {
        var holding = At(10m, 100m);
        var created = holding.CreatedAt;

        Clock.Advance(TimeSpan.FromMinutes(5));
        holding.Merge(10m, Money.Usd(150m), Clock);

        holding.CreatedAt.ShouldBe(created);
        holding.UpdatedAt.ShouldBe(created.AddMinutes(5));
    }

    [Fact]
    public void ClearDomainEvents_EmptiesTheList()
    {
        var holding = At(10m, 100m);
        holding.Remove();

        holding.ClearDomainEvents();

        holding.DomainEvents.ShouldBeEmpty();
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/StockPortfolio.Modules.Portfolio.UnitTests --filter HoldingTests`
Expected: FAIL with CS0246 — `Holding` does not exist.

- [ ] **Step 3: Write the entity**

```csharp
// src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Domain/Holding.cs
using OneOf;
using OneOf.Types;
using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.DomainEvents;

namespace StockPortfolio.Modules.Portfolio.Domain;

/// <summary>One user's position in one ticker. A unique index on (user_id, ticker) keeps it one row.</summary>
public sealed class Holding
{
    /// <summary>One unit of the column's precision; below this a quantity rounds to zero on store.</summary>
    private const decimal MinimumQuantity = 0.000001m;

    /// <summary>Decimal places the average is rounded to, matching numeric(18,6).</summary>
    private const int PriceScale = 6;

    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>The only constructor. Assigns and nothing else; EF binds it by name for every row.</summary>
    private Holding(
        HoldingId id,
        Guid userId,
        Ticker ticker,
        decimal quantity,
        bool isVisible,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        UserId = userId;
        Ticker = ticker;
        Quantity = quantity;
        IsVisible = isVisible;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>Gets the identity of the holding.</summary>
    public HoldingId Id { get; private set; }

    /// <summary>Gets the owning user. A plain Guid: Portfolio does not own the Identity module's UserId.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Gets the symbol held, already upper-cased by Ticker.Create.</summary>
    public Ticker Ticker { get; private set; }

    /// <summary>Gets the number of shares, which may be fractional.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>Gets the weighted average purchase price. Omitted from the constructor — efcore#31621.</summary>
    public Money AveragePrice { get; private set; }

    /// <summary>Gets whether the dashboard shows this position. Always true until Phase 5 adds the toggle.</summary>
    public bool IsVisible { get; private set; }

    /// <summary>Gets the instant the position was opened.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the instant the position last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Gets the events raised since the last save. Mapped out by HoldingConfiguration.</summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>Opens a position. The only way to build a Holding.</summary>
    public static OneOf<Holding, InvalidInput> Create(
        Guid userId,
        Ticker ticker,
        decimal quantity,
        Money purchasePrice,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Validate(quantity, purchasePrice).TryPickT1(out var invalid, out _))
        {
            return invalid;
        }

        var now = clock.GetUtcNow();

        var holding = new Holding(HoldingId.New(), userId, ticker, quantity, isVisible: true, now, now);

        // Assigned after construction because a complex type cannot be a constructor parameter
        // (efcore#31621, open and pushed back to Backlog). private set is reachable from inside the type.
        holding.AveragePrice = purchasePrice;

        return holding;
    }

    /// <summary>Merges a further purchase: quantities sum, price becomes the weighted average.</summary>
    public OneOf<Success, InvalidInput> Merge(decimal quantity, Money purchasePrice, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Guard(quantity, purchasePrice).TryPickT1(out var invalid, out _))
        {
            return invalid;
        }

        var total = Quantity + quantity;

        // .Amount arithmetic, not Money's operators: Money has no division, and Add would throw on a
        // currency mismatch that Guard has already turned into a result case.
        var weighted = ((AveragePrice.Amount * Quantity) + (purchasePrice.Amount * quantity)) / total;

        Quantity = total;
        AveragePrice = new Money(
            Math.Round(weighted, PriceScale, MidpointRounding.ToEven),
            AveragePrice.Currency);
        UpdatedAt = clock.GetUtcNow();

        return new Success();
    }

    /// <summary>Corrects a mistyped entry. Replaces, never averages — a typo is not a second purchase.</summary>
    public OneOf<Success, InvalidInput> Correct(decimal quantity, Money purchasePrice, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Guard(quantity, purchasePrice).TryPickT1(out var invalid, out _))
        {
            return invalid;
        }

        Quantity = quantity;
        AveragePrice = purchasePrice;
        UpdatedAt = clock.GetUtcNow();

        return new Success();
    }

    /// <summary>Records that the position is being closed. The repository performs the delete.</summary>
    public void Remove() => _domainEvents.Add(new HoldingRemoved(UserId, Ticker.Value));

    /// <summary>Drops the raised events, once the interceptor has taken them.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Validates an incoming purchase against the position's existing currency.</summary>
    private OneOf<Success, InvalidInput> Guard(decimal quantity, Money purchasePrice)
    {
        // Checked BEFORE any Money arithmetic: EnsureSameCurrency throws, and a throw here would
        // surface as a 500 instead of the 400 this rule is meant to produce.
        if (!string.Equals(purchasePrice.Currency, AveragePrice.Currency, StringComparison.Ordinal))
        {
            return new InvalidInput(
                "price",
                $"This position is held in {AveragePrice.Currency}; {purchasePrice.Currency} cannot be mixed in.");
        }

        return Validate(quantity, purchasePrice);
    }

    /// <summary>The rules that hold whether or not a position already exists.</summary>
    private static OneOf<Success, InvalidInput> Validate(decimal quantity, Money purchasePrice)
    {
        if (quantity < MinimumQuantity)
        {
            return new InvalidInput(
                "quantity",
                $"Quantity must be at least {MinimumQuantity.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)}.");
        }

        if (purchasePrice.Amount <= 0m)
        {
            return new InvalidInput("price", "Purchase price must be greater than zero.");
        }

        return new Success();
    }
}
```

Four things worth reading twice:

- **The constructor is guard-free**, and `AveragePrice` is not one of its parameters. EF binds by name for
  materialisation and would otherwise re-run every guard on every row of every `SELECT`.
- **`Guard` is an instance method and `Validate` is static.** `Create` has no existing currency to compare
  against, so it calls `Validate`; `Merge` and `Correct` call `Guard`, which adds the currency comparison.
- ⛔ **`_domainEvents` is a field with a `IReadOnlyList` projection**, so `DomainShapeTests` rule 3 (no public
  setter on a domain type) passes. Task 7 maps it out with `builder.Ignore`. **Withdrawn by §0.0** — there is
  no event collection, so there is nothing to project and nothing to `Ignore`.
- ⛔ **`Remove()` does not delete.** It records the event; `HoldingRepository.RemoveAsync` deletes. Task 12's
  handler calls both, and `Remove_RaisesHoldingRemoved_Once` is what stops the pair drifting apart.
  **Withdrawn by §0.0** — `HoldingRepository.RemoveAsync` deletes, and that is the whole of removal.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/StockPortfolio.Modules.Portfolio.UnitTests`
Expected: PASS — 12 `TickerTests` + 15 `HoldingTests`.

- [ ] **Step 5: Confirm the domain-shape rule went live and passes**

Run: `dotnet test tests/StockPortfolio.Architecture.Tests --filter DomainType_ExposesNoPublicSetter`
Expected: `Portfolio.Domain` runs instead of skipping, and passes. Every property is `{ get; private set; }`
or get-only.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Domain/Holding.cs tests/StockPortfolio.Modules.Portfolio.UnitTests/HoldingTests.cs
git commit -m "The Holding aggregate: merge averages, correct replaces, only removal is worth telling anyone"
```

---

### Task 7: Persistence — context, converters, configuration, design-time factory

**Files:**
- Create: `.../Portfolio.Infrastructure/Persistence/PortfolioDbContext.cs`
- Create: `.../Portfolio.Infrastructure/Persistence/Converters/HoldingIdConverter.cs`
- Create: `.../Portfolio.Infrastructure/Persistence/Converters/TickerConverter.cs`
- Create: `.../Portfolio.Infrastructure/Persistence/Configurations/HoldingConfiguration.cs`
- Create: `.../Portfolio.Infrastructure/Persistence/PortfolioDbContextFactory.cs`
- Modify: `src/Api/appsettings.Development.json`

**Interfaces:**
- Produces: `internal sealed class PortfolioDbContext` with `DbSet<Holding> Holdings`, consts
  `SchemaName = "portfolio"` and `MigrationsHistoryTableName = "__EFMigrationsHistory"`.
- Consumed by: Task 8 (migration), Task 9 (repository), Task 13 (module seam).

- [ ] **Step 1: The two converters**

```csharp
// .../Persistence/Converters/HoldingIdConverter.cs
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Converters;

/// <summary>Maps the strongly-typed HoldingId to the plain Guid the database stores.</summary>
internal sealed class HoldingIdConverter : ValueConverter<HoldingId, Guid>
{
    public HoldingIdConverter()
        : base(id => id.Value, value => new HoldingId(value))
    {
    }
}
```

```csharp
// .../Persistence/Converters/TickerConverter.cs
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Converters;

/// <summary>Maps Ticker to the plain string the database stores; the stored value is already canonical.</summary>
internal sealed class TickerConverter : ValueConverter<Ticker, string>
{
    public TickerConverter()
        : base(ticker => ticker.Value, value => new Ticker(value))
    {
    }
}
```

Both live in `.Infrastructure`, not beside their types — that split is what keeps EF out of `.Domain`. They
are hand-written because a shared generic converter is impossible: `docs/deferred-work.md` records that a
`static abstract` interface member cannot be invoked in an expression tree, and `ValueConverter` takes
`Expression<Func<,>>`. Revisit at roughly eight id types, with a source generator.

The reader direction calls `new Ticker(value)` and deliberately skips `Create` — the stored value was
canonicalised on the way in, and re-validating on every row is the guard-in-the-constructor trap wearing a
different hat.

- [ ] **Step 2: The context**

```csharp
// .../Persistence/PortfolioDbContext.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Converters;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

/// <summary>The Portfolio module's only DbContext.</summary>
internal sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : DbContext(options)
{
    /// <summary>The Postgres schema this context owns.</summary>
    internal const string SchemaName = "portfolio";

    /// <summary>The migration history table name.</summary>
    internal const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<Holding> Holdings => Set<Holding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(PortfolioDbContext).Assembly,
            predicate: t => t.Namespace is not null
                && t.Namespace.StartsWith("StockPortfolio.Modules.Portfolio", StringComparison.Ordinal));
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Throw(CoreEventId.SkippedEntityTypeConfigurationWarning));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<HoldingId>().HaveConversion<HoldingIdConverter>();
        configurationBuilder.DefaultTypeMapping<HoldingId>().HasConversion<HoldingIdConverter>();

        configurationBuilder.Properties<Ticker>().HaveConversion<TickerConverter>();
        configurationBuilder.DefaultTypeMapping<Ticker>().HasConversion<TickerConverter>();
    }
}
```

**Both `Properties<T>()` and `DefaultTypeMapping<T>()` for both types.** The second line is the one people
miss: without it, a `Ticker` used anywhere other than a mapped entity property — such as the `ticker`
parameter of `GetAsync`'s LINQ closure in Task 14 — has no mapping and throws at runtime, long after
model building succeeded.

**The namespace prefix in the predicate is load-bearing.** `"StockPortfolio.Modules.Portfolio"` — get it
wrong and `HoldingConfiguration` is silently skipped. `ConfigureWarnings(...Throw(SkippedEntityTypeConfigurationWarning))`
turns that silence into a startup failure.

- [ ] **Step 3: The configuration**

```csharp
// .../Persistence/Configurations/HoldingConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Configurations;

/// <summary>Maps Holding to portfolio.holdings.</summary>
internal sealed class HoldingConfiguration : IEntityTypeConfiguration<Holding>
{
    internal const string TableName = "holdings";

    /// <summary>The one real guarantee behind the merge rule: a C# check cannot survive two requests.</summary>
    internal const string UserTickerUniqueIndexName = "ix_holdings_user_id_ticker";

    /// <summary>Fractional shares exist, so an average of $125.333333 must not round to $125.33.</summary>
    private const int MoneyPrecision = 18;

    private const int MoneyScale = 6;

    private const int CurrencyLength = 3;

    public void Configure(EntityTypeBuilder<Holding> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(h => h.Id);

        // The domain generates a UUIDv7 in HoldingId.New(); the database must not touch it.
        builder.Property(h => h.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(h => h.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(h => h.Ticker)
            .HasColumnName("ticker")
            .HasMaxLength(Ticker.MaxLength)
            .IsRequired();

        builder.Property(h => h.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(MoneyPrecision, MoneyScale)
            .IsRequired();

        // ComplexProperty, not OwnsOne: an owned type is an entity type and carries identity, so
        // assigning one Money instance to two properties throws on save. Complex types copy by value.
        // Mapped member by member because Money's properties are get-only and are therefore not
        // mapped by convention - a bare ComplexProperty(h => h.AveragePrice) fails at model build.
        builder.ComplexProperty(h => h.AveragePrice, price =>
        {
            price.Property(m => m.Amount)
                .HasColumnName("avg_price_amount")
                .HasPrecision(MoneyPrecision, MoneyScale);

            price.Property(m => m.Currency)
                .HasColumnName("avg_price_currency")
                .HasMaxLength(CurrencyLength)
                .IsFixedLength();
        });

        builder.Property(h => h.IsVisible)
            .HasColumnName("is_visible")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(h => h.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(h => h.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(h => new { h.UserId, h.Ticker })
            .IsUnique()
            .HasDatabaseName(UserTickerUniqueIndexName);

        // ⛔ WITHDRAWN by §0.0 — there is no DomainEvents member to ignore.
        // Raised events live in memory between a mutation and the dispatch interceptor. Never a column.
        builder.Ignore(h => h.DomainEvents);
    }
}
```

**`HasColumnName` on every property**, matching `UserConfiguration`. Npgsql does not snake-case by
convention, so leaving them off gives `"UserId"`, `"Quantity"`, `"CreatedAt"` — quoted, mixed-case columns
that disagree with `er-diagram.md` and with every other table in the database.

- [ ] **Step 4: The design-time factory and the missing connection string**

```csharp
// .../Persistence/PortfolioDbContextFactory.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

/// <summary>Lets dotnet ef build the model without any local configuration.</summary>
internal sealed class PortfolioDbContextFactory : IDesignTimeDbContextFactory<PortfolioDbContext>
{
    /// <summary>Never connected to: migrations are scaffolded from the model, not from the server.</summary>
    private const string DesignTimeConnectionString =
        "Host=localhost;Database=stockportfolio-design-time;Username=migrator;Password=unused";

    public PortfolioDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseNpgsql(
                DesignTimeConnectionString,
                npg => npg.MigrationsHistoryTable(
                    PortfolioDbContext.MigrationsHistoryTableName,
                    PortfolioDbContext.SchemaName))
            .Options);
}
```

Mirror `Identity`'s `DesignTimeFactory.cs`. Then add the key the spec's *"Infrastructure delta: **None**"*
missed — `src/Api/appsettings.Development.json` currently has `Identity` and `Redis` only:

```json
    "Portfolio": "Host=localhost;Port=5432;Database=stockportfolio;Username=portfolio_svc;Password=portfolio_dev_only;Maximum Pool Size=2"
```

⚠️ Without it, `AddPortfolioModule`'s eager connection-string guard (Task 13) throws — and because
`dotnet ef --startup-project src/Api` **builds the host**, that breaks every migration command in Task 8,
before it ever breaks at runtime. `docker-compose.yml` and `infra/main.bicep` already supply the key; only the
local development file was missing it.

- [ ] **Step 5: Verify the model builds**

Run: `dotnet build`
Expected: clean, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/Persistence src/Api/appsettings.Development.json
git commit -m "Map holdings: explicit columns, a complex price, and the index the merge rule rests on"
```

---

### Task 8: The migration, and the constructor-binding test that guards it

**Files:**
- Create: `.../Portfolio.Infrastructure/Persistence/Migrations/*_InitialPortfolio.cs` (+ `.Designer.cs`, snapshot)
- Create: `tests/StockPortfolio.Modules.Portfolio.UnitTests/EfModelTests.cs`

**Interfaces:**
- Produces: migration `InitialPortfolio`, creating `portfolio.holdings` and
  `portfolio."__EFMigrationsHistory"`.

- [ ] **Step 1: Write the failing model tests first**

`Identity`'s `EfConstructorBindingTests` builds the model against a fake connection string with no container
at all, which is exactly what makes it a unit test. Copy that approach — but **not verbatim**: `Holding`'s
`AveragePrice` is set after construction, so the assertion is over *scalar* properties only.

```csharp
// tests/StockPortfolio.Modules.Portfolio.UnitTests/EfModelTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

namespace StockPortfolio.Tests;

/// <summary>The model must build, and it must bind the one constructor Holding has.</summary>
public sealed class EfModelTests
{
    private const string ModelOnly = "Host=localhost;Database=model-only;Username=none;Password=none";

    private static IEntityType HoldingEntity()
    {
        using var context = new PortfolioDbContext(
            new DbContextOptionsBuilder<PortfolioDbContext>().UseNpgsql(ModelOnly).Options);

        return context.Model.FindEntityType(typeof(Holding))!;
    }

    // Renaming a constructor parameter without renaming its property leaves no bindable constructor,
    // and with no parameterless fallback the WHOLE model fails to build at startup.
    [Fact]
    public void Holding_BindsEveryScalarProperty_ThroughTheConstructor()
    {
        var bound = typeof(Holding)
            .GetConstructors(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)
            .ShouldHaveSingleItem()
            .GetParameters()
            .Select(parameter => parameter.Name!)
            .ToList();

        bound.ShouldBe(
            ["id", "userId", "ticker", "quantity", "isVisible", "createdAt", "updatedAt"],
            ignoreOrder: true,
            "EF binds by NAME. AveragePrice is absent on purpose — a complex type cannot be a "
                + "constructor parameter (efcore#31621) — and the factory assigns it afterwards.");
    }

    [Fact]
    public void Holding_HasNoParameterlessConstructor() =>
        typeof(Holding)
            .GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public,
                Type.EmptyTypes)
            .ShouldBeNull("A half-built Holding must not be representable; Create is the only way in.");

    // The cost of mapping Money member by member: a member added later is silently unmapped.
    [Fact]
    public void AveragePrice_MapsEveryMemberOfMoney()
    {
        var mapped = HoldingEntity()
            .GetComplexProperties()
            .ShouldHaveSingleItem()
            .ComplexType
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        var declared = typeof(StockPortfolio.Shared.Kernel.Money)
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        mapped.ShouldBe(
            declared,
            ignoreOrder: true,
            "HoldingConfiguration maps Money member by member, because Money's get-only properties "
                + "are not mapped by convention. A member added to Money is therefore silently "
                + "unmapped until a .Property() line is added beside it.");
    }

    [Fact]
    public void Holdings_AreKeyedOnUserAndTicker_Uniquely() =>
        HoldingEntity()
            .GetIndexes()
            .ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                index => index.IsUnique.ShouldBeTrue(),
                index => index.Properties.Select(p => p.Name).ShouldBe(["UserId", "Ticker"]));

    // ⛔ WITHDRAWN by §0.0 — Holding has no DomainEvents member, so there is nothing to assert unmapped.
    [Fact]
    public void DomainEvents_IsNotMapped() =>
        HoldingEntity()
            .GetProperties()
            .Select(property => property.Name)
            .ShouldNotContain(nameof(Holding.DomainEvents));
}
```

- [ ] **Step 2: Run them**

Run: `dotnet test tests/StockPortfolio.Modules.Portfolio.UnitTests --filter EfModelTests`
Expected: PASS. If `AveragePrice_MapsEveryMemberOfMoney` fails with *"No suitable constructor was found for
the type 'Holding.AveragePrice#Money'"*, the `ComplexProperty` lambda in Task 7 lost its `.Property()` calls —
that is the model-build failure §2.3 exists to prevent, and it happens at startup, not first query.

- [ ] **Step 3: Scaffold the migration**

```bash
dotnet ef migrations add InitialPortfolio --context PortfolioDbContext --output-dir Persistence/Migrations --project src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure --startup-project src/Api
```

- [ ] **Step 4: Read the generated SQL before trusting it**

Run: `dotnet ef migrations script --context PortfolioDbContext --project src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure --startup-project src/Api`

Check, by eye, all five:
- `CREATE TABLE portfolio.holdings` — not `public.holdings`
- every column snake-cased: `id, user_id, ticker, quantity, avg_price_amount, avg_price_currency, is_visible, created_at, updated_at`
- `avg_price_amount numeric(18,6)` and `quantity numeric(18,6)` — **not** bare `numeric`
- `avg_price_currency character(3)`
- `CREATE UNIQUE INDEX ix_holdings_user_id_ticker`

⚠️ Without `.HasPrecision(18, 6)` Npgsql maps `decimal` to unconstrained `numeric`, which *works* — and then
any later `HasPrecision(18,2)` silently truncates every stored average on the next migration.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/Persistence/Migrations tests/StockPortfolio.Modules.Portfolio.UnitTests/EfModelTests.cs
git commit -m "InitialPortfolio, and the model tests that fail at build time rather than at 3am"
```

---

### Task 9: `IHoldingRepository` and its implementation

**Files:**
- Create: `.../Portfolio.Application/Abstractions/IHoldingRepository.cs`
- Create: `.../Portfolio.Infrastructure/Persistence/HoldingRepository.cs`

**Interfaces:**
- Produces: `FindAsync(Guid, Ticker, CancellationToken) → Task<Holding?>`;
  `FindByIdAsync(Guid, HoldingId, CancellationToken) → Task<Holding?>`;
  `ListAsync(Guid, CancellationToken) → Task<IReadOnlyList<Holding>>`;
  `AddAsync(Holding, CancellationToken)`; `UpdateAsync(Holding, CancellationToken)`;
  `RemoveAsync(Holding, CancellationToken)`.
- Consumed by: Task 12 (all four handlers).

- [ ] **Step 1: State the commit point on the interface, before any handler exists**

`add-vertical-slice` step 3 makes this the question to answer first, and §2.7 answered it: dispatch happens
after the save, so Portfolio's repositories self-commit exactly like Identity's.

```csharp
// .../Portfolio.Application/Abstractions/IHoldingRepository.cs
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Application.Abstractions;

/// <summary>Stores and finds holdings. Every write method here commits before it returns.</summary>
public interface IHoldingRepository
{
    /// <summary>Finds this user's position in a ticker, tracked so the handler can mutate it.</summary>
    Task<Holding?> FindAsync(Guid userId, Ticker ticker, CancellationToken ct);

    /// <summary>Finds one of this user's holdings by id. Scoped to the user: another user's id is not found.</summary>
    Task<Holding?> FindByIdAsync(Guid userId, HoldingId id, CancellationToken ct);

    /// <summary>Lists this user's holdings, newest position first.</summary>
    Task<IReadOnlyList<Holding>> ListAsync(Guid userId, CancellationToken ct);

    /// <summary>Inserts a holding and commits.</summary>
    Task AddAsync(Holding holding, CancellationToken ct);

    /// <summary>Persists changes made to a tracked holding and commits.</summary>
    Task UpdateAsync(Holding holding, CancellationToken ct);

    /// <summary>Deletes a holding and commits, dispatching any event it raised.</summary>
    Task RemoveAsync(Holding holding, CancellationToken ct);
}
```

⚠️ **`FindByIdAsync` takes the user id as well as the holding id, and that is a security control, not
tidiness.** It is what makes another user's holding return **404 rather than 403** — a 403 confirms the id
exists. Every read is filtered by `userId` at the repository, so no handler can forget.

- [ ] **Step 2: Implement it**

```csharp
// .../Portfolio.Infrastructure/Persistence/HoldingRepository.cs
using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

internal sealed class HoldingRepository(PortfolioDbContext context) : IHoldingRepository
{
    public async Task<Holding?> FindAsync(Guid userId, Ticker ticker, CancellationToken ct)
        => await context.Holdings.FirstOrDefaultAsync(h => h.UserId == userId && h.Ticker == ticker, ct);

    public async Task<Holding?> FindByIdAsync(Guid userId, HoldingId id, CancellationToken ct)
        => await context.Holdings.FirstOrDefaultAsync(h => h.UserId == userId && h.Id == id, ct);

    public async Task<IReadOnlyList<Holding>> ListAsync(Guid userId, CancellationToken ct)
        => await context.Holdings
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Holding holding, CancellationToken ct)
    {
        context.Holdings.Add(holding);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Holding holding, CancellationToken ct)
        => await context.SaveChangesAsync(ct);

    public async Task RemoveAsync(Holding holding, CancellationToken ct)
    {
        context.Holdings.Remove(holding);
        await context.SaveChangesAsync(ct);
    }
}
```

⚠️ **No `AsNoTracking()` on any of these.** `ListAsync` looks like a read that would benefit — it does not.
`ChangeTracker.Entries<T>()` only sees tracked entities, so a no-tracking default anywhere on this context
means a command handler saves nothing *and* dispatches nothing, with no error at all. The genuinely untracked
reads are in Task 14, on a separate type.

`UpdateAsync` takes the holding it does not use, and that is deliberate: the parameter says at the call site
which aggregate is being persisted, and it keeps the interface honest if a future implementation needs to
`Attach`.

- [ ] **Step 3: Verify**

Run: `dotnet build`
Expected: clean. `LayerReferenceTests` still passes — `.Application` gained no persistence reference, because
the interface names only `Holding`, `Ticker` and `HoldingId`.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Application/Abstractions src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/Persistence/HoldingRepository.cs
git commit -m "Holding repository: every read scoped to the user, so 404 is structural"
```

---

### Task 10: Domain-event dispatch — ⛔ WITHDRAWN

> **Do not build this task.** Withdrawn by §0.0 along with Task 1. There is no `IDomainEventPublisher` to
> implement, no `IDomainEvent` to collect and no cross-module event to dispatch, so the interceptor, the
> publisher and the six tests are all withdrawn. The three EF facts recorded here are still true and still
> worth keeping — a `Deleted` entity becomes `Detached` after a successful save, so anything reading the
> change tracker post-save reads nothing; `ChangeTracker.Entries<T>()` is a live projection and must be
> `.ToList()`ed before enumeration; the interceptor signatures are as printed. Nothing in Phase 2 now needs
> them. Task numbering is preserved.
>
> The original task is left below, unedited, as the record of what was planned and rejected.

The seam every later cross-module event goes through. §2.7 settled *when* it fires; this task is *how*.

**Files:**
- Create: `.../Portfolio.Infrastructure/Persistence/DispatchDomainEventsInterceptor.cs`
- Create: `.../Portfolio.Infrastructure/Persistence/DomainEventPublisher.cs`
- Create: `tests/StockPortfolio.Modules.Portfolio.UnitTests/DispatchDomainEventsInterceptorTests.cs`

**Interfaces:**
- Produces: `internal sealed class DispatchDomainEventsInterceptor : SaveChangesInterceptor` (scoped);
  `internal sealed class DomainEventPublisher : IDomainEventPublisher`.
- Consumed by: Task 13 (registered inside `AddPortfolioModule`), Phase 4 (Alerts registers a handler).

**The signatures, read off `Microsoft.EntityFrameworkCore` 10.0.10 by reflection rather than from docs:**

```csharp
public virtual ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
public virtual ValueTask<int>                     SavedChangesAsync (SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
public virtual Task                               SaveChangesFailedAsync  (DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
public virtual Task                               SaveChangesCanceledAsync(DbContextEventData eventData,      CancellationToken cancellationToken = default)
```

**Why it is two hooks and not one.** `InternalEntryBase.AcceptChanges` maps `Deleted → Detached`, so after a
successful save the deleted `Holding` is **gone from `ChangeTracker.Entries<T>()`**. `HoldingRemoved` is
raised by the one operation whose entity disappears. Collect before; publish after.

**Why the collection is `.ToList()`ed first.** `ChangeTracker.Entries<T>()` is a lazy projection over the
state manager's live dictionaries — there is no snapshot anywhere in the path. Enumerating it while anything
mutates the context throws `InvalidOperationException: Collection was modified`. This was reproduced, not
inferred.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/StockPortfolio.Modules.Portfolio.UnitTests/DispatchDomainEventsInterceptorTests.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.DomainEvents;

namespace StockPortfolio.Tests;

public sealed class DispatchDomainEventsInterceptorTests
{
    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

    private sealed class RecordingPublisher : IDomainEventPublisher
    {
        public List<IDomainEvent> Published { get; } = [];

        public int Calls { get; private set; }

        public Task PublishAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken ct)
        {
            Calls++;
            Published.AddRange(domainEvents);
            return Task.CompletedTask;
        }
    }

    private static Holding Removed()
    {
        var holding = Holding.Create(Guid.CreateVersion7(), Ticker.Create("AAPL").AsT0, 10m, Money.Usd(100m), Clock).AsT0;
        holding.Remove();
        return holding;
    }

    // The whole reason collection happens pre-save: a Deleted entity is Detached afterwards.
    [Fact]
    public async Task Collect_TakesEventsFromTheEntity_AndClearsThem()
    {
        var publisher = new RecordingPublisher();
        var interceptor = new DispatchDomainEventsInterceptor(publisher);
        var holding = Removed();

        interceptor.Collect([holding]);

        holding.DomainEvents.ShouldBeEmpty("cleared on collection, so a second save cannot republish");

        await interceptor.PublishCollectedAsync(CancellationToken.None);

        publisher.Published.ShouldHaveSingleItem().ShouldBeOfType<HoldingRemoved>();
    }

    [Fact]
    public async Task PublishCollected_Twice_PublishesOnce()
    {
        var publisher = new RecordingPublisher();
        var interceptor = new DispatchDomainEventsInterceptor(publisher);

        interceptor.Collect([Removed()]);

        await interceptor.PublishCollectedAsync(CancellationToken.None);
        await interceptor.PublishCollectedAsync(CancellationToken.None);

        publisher.Calls.ShouldBe(1, "the pending list is cleared before publishing, not after");
    }

    [Fact]
    public async Task Discard_ThenPublish_PublishesNothing()
    {
        var publisher = new RecordingPublisher();
        var interceptor = new DispatchDomainEventsInterceptor(publisher);

        interceptor.Collect([Removed()]);
        interceptor.Discard();

        await interceptor.PublishCollectedAsync(CancellationToken.None);

        publisher.Calls.ShouldBe(0, "a save that failed or was cancelled must publish nothing");
    }

    [Fact]
    public async Task NoEvents_PublishesNothing()
    {
        var publisher = new RecordingPublisher();
        var interceptor = new DispatchDomainEventsInterceptor(publisher);

        var holding = Holding.Create(Guid.CreateVersion7(), Ticker.Create("AAPL").AsT0, 10m, Money.Usd(100m), Clock).AsT0;

        interceptor.Collect([holding]);
        await interceptor.PublishCollectedAsync(CancellationToken.None);

        publisher.Calls.ShouldBe(0, "an ordinary add must not cost a publisher round trip");
    }

    // The sync path must be loud, not silently undispatched.
    [Fact]
    public void SavingChanges_Synchronous_Throws() =>
        Should.Throw<NotSupportedException>(() =>
            new DispatchDomainEventsInterceptor(new RecordingPublisher())
                .SavingChanges(null!, default));
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dotnet test tests/StockPortfolio.Modules.Portfolio.UnitTests --filter DispatchDomainEventsInterceptorTests`
Expected: FAIL with CS0246.

- [ ] **Step 3: Write the interceptor**

```csharp
// .../Portfolio.Infrastructure/Persistence/DispatchDomainEventsInterceptor.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel.DomainEvents;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

/// <summary>Collects domain events before a save and publishes them once it has committed.</summary>
internal sealed class DispatchDomainEventsInterceptor(IDomainEventPublisher publisher) : SaveChangesInterceptor
{
    private readonly List<IDomainEvent> _pending = [];

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        // Collected HERE, not in SavedChangesAsync: AcceptChanges maps Deleted to Detached, so the
        // one entity that raises an event has vanished from the change tracker by then.
        if (eventData?.Context is not null)
        {
            Collect(eventData.Context.ChangeTracker.Entries<Holding>().Select(entry => entry.Entity).ToList());
        }

        // The incoming result, never `default`: an earlier interceptor may have suppressed the save.
        return ValueTask.FromResult(result);
    }

    /// <inheritdoc/>
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await PublishCollectedAsync(cancellationToken);

        return result;
    }

    /// <inheritdoc/>
    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Discard();

        return Task.CompletedTask;
    }

    /// <summary>Refuses the synchronous path, which would commit without ever dispatching.</summary>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result) =>
        throw new NotSupportedException(
            "Portfolio dispatches domain events from the async save path only. Use SaveChangesAsync.");

    /// <summary>Takes the events off the entities and holds them until the save commits.</summary>
    internal void Collect(IReadOnlyCollection<Holding> holdings)
    {
        ArgumentNullException.ThrowIfNull(holdings);

        foreach (var holding in holdings)
        {
            if (holding.DomainEvents.Count == 0)
            {
                continue;
            }

            _pending.AddRange(holding.DomainEvents);

            // Cleared as they are taken: a second SaveChangesAsync on the same context must not
            // re-collect and re-publish what this one already owns.
            holding.ClearDomainEvents();
        }
    }

    /// <summary>Publishes and forgets whatever was collected.</summary>
    internal async Task PublishCollectedAsync(CancellationToken ct)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        // Copied and cleared BEFORE publishing: a handler that triggers another save must not find
        // these still pending and publish them twice.
        var dispatching = _pending.ToArray();
        _pending.Clear();

        await publisher.PublishAsync(dispatching, ct);
    }

    /// <summary>Drops collected events after a save that did not commit.</summary>
    internal void Discard() => _pending.Clear();
}
```

Four decisions in there:

- **`SavingChanges` throws.** EF provably never calls the sync path during an async save — they are dispatched
  from two separate, non-cross-calling logger extensions — so this is safe and it makes an accidental
  `SaveChanges()` loud instead of silently undispatched. ⚠️ It hard-fails any synchronous save anywhere:
  `Migrator`, seeding, EF design-time tooling, a test helper. None exists today; the throw is what stops one
  appearing quietly.
- **`SaveChangesCanceledAsync` also discards.** Not in the spec. A cancelled request would otherwise leave
  events pending on a scoped interceptor, and the next save in that scope would publish them as if they had
  committed.
- **Clear-before-publish**, which the spec gets right for the wrong hook — it matters here because the
  interceptor is scoped and survives multiple saves in one request.
- **No drain loop and no depth cap.** Both exist in the spec to stop a handler-raised event recursing. With
  dispatch after the save, nothing raised during dispatch can join it, so there is no cycle to cap.

- [ ] **Step 4: Write the publisher**

```csharp
// .../Portfolio.Infrastructure/Persistence/DomainEventPublisher.cs
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Shared.Kernel.DomainEvents;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

/// <summary>Resolves and invokes every handler registered for each event's concrete type.</summary>
internal sealed class DomainEventPublisher(IServiceProvider services) : IDomainEventPublisher
{
    public async Task PublishAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvents);

        foreach (var domainEvent in domainEvents)
        {
            // Closed over the RUNTIME type: IDomainEventHandler<HoldingRemoved> is not resolvable
            // through the IDomainEvent-typed variable, so the lookup has to be reflective.
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());

            foreach (var handler in services.GetServices(handlerType))
            {
                await (Task)handlerType
                    .GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!
                    .Invoke(handler, [domainEvent, ct])!;
            }
        }
    }
}
```

Zero handlers is the correct Phase 2 state and must not throw: `GetServices` returns an empty sequence, the
loop does nothing, and Phase 4 adds the first registration. That is what
`NoEvents_PublishesNothing` and the Phase 4 test `HoldingRemoved_ClearsCooldown` between them pin.

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/StockPortfolio.Modules.Portfolio.UnitTests --filter DispatchDomainEventsInterceptorTests`
Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/Persistence/DispatchDomainEventsInterceptor.cs src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/Persistence/DomainEventPublisher.cs tests/StockPortfolio.Modules.Portfolio.UnitTests/DispatchDomainEventsInterceptorTests.cs
git commit -m "Collect events before the save, publish them after it commits"
```

---

### Task 11: The four use cases

**Files:**
- Create: `.../Portfolio.Application/HoldingSummary.cs`
- Create: `.../Portfolio.Application/Holdings/Commands/AddHolding/{AddHoldingCommand,AddHoldingCommandHandler,HoldingCreated,HoldingMerged,UnknownTicker}.cs`
- Create: `.../Portfolio.Application/Holdings/Commands/UpdateHolding/{UpdateHoldingCommand,UpdateHoldingCommandHandler}.cs`
- Create: `.../Portfolio.Application/Holdings/Commands/RemoveHolding/{RemoveHoldingCommand,RemoveHoldingCommandHandler}.cs`
- Create: `.../Portfolio.Application/Holdings/Queries/GetHoldings/{GetHoldingsQuery,GetHoldingsQueryHandler}.cs`

**Interfaces:**
- Produces, and these exact closed generics are what Task 13 registers and Task 16 injects:

```csharp
ICommandHandler<AddHoldingCommand,    OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>>
ICommandHandler<UpdateHoldingCommand, OneOf<HoldingSummary, NotFound, InvalidInput>>
ICommandHandler<RemoveHoldingCommand, OneOf<Success, NotFound>>
IQueryHandler<GetHoldingsQuery,       IReadOnlyList<HoldingSummary>>
```

- [ ] **Step 1: The shared success payload**

At the `Application` root, the position `TokenPair.cs` occupies in Identity, because three use cases return it.

```csharp
// .../Portfolio.Application/HoldingSummary.cs
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Application;

/// <summary>One position as the client sees it. Money is computed here, never in the browser.</summary>
public sealed record HoldingSummary(
    Guid Id,
    string Ticker,
    decimal Quantity,
    Money AveragePrice,
    Money Invested,
    bool IsVisible,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Projects a holding, computing what it cost.</summary>
    public static HoldingSummary From(Holding holding)
    {
        ArgumentNullException.ThrowIfNull(holding);

        return new HoldingSummary(
            holding.Id.Value,
            holding.Ticker.Value,
            holding.Quantity,
            holding.AveragePrice,
            holding.AveragePrice.Multiply(holding.Quantity),
            holding.IsVisible,
            holding.UpdatedAt);
    }
}
```

`Invested` is computed server-side and both `Money` members serialise as strings through Task 2's converter.
`IsVisible` is on the wire from day one so Phase 5 adds behaviour, not a contract change (§2.9).

- [ ] **Step 2: `AddHolding` — the only use case with two successes**

```csharp
// Holdings/Commands/AddHolding/AddHoldingCommand.cs
namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

/// <summary>Buys a quantity of a ticker, opening or adding to a position.</summary>
public sealed record AddHoldingCommand(Guid UserId, string Ticker, decimal Quantity, decimal Price);
```

```csharp
// Holdings/Commands/AddHolding/HoldingCreated.cs
namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

/// <summary>A position that did not exist before. The endpoint answers 201.</summary>
public sealed record HoldingCreated(HoldingSummary Holding);
```

```csharp
// Holdings/Commands/AddHolding/HoldingMerged.cs
namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

/// <summary>A purchase folded into an existing position. The endpoint answers 200.</summary>
public sealed record HoldingMerged(HoldingSummary Holding);
```

```csharp
// Holdings/Commands/AddHolding/UnknownTicker.cs
namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

/// <summary>A symbol this application will not accept. Phase 2 checks shape; Phase 3 checks existence.</summary>
public sealed record UnknownTicker(string Ticker);
```

`UnknownTicker` is separate from `InvalidInput` deliberately. They are the same 400 today, and they stop being
the same the moment Phase 3 replaces the shape check with a real symbol lookup — at which point this becomes
"we asked the provider and there is no such stock", which is a different sentence and possibly a different
status. Collapsing them now would mean re-splitting them then, and every `.Match` call site would change.

```csharp
// Holdings/Commands/AddHolding/AddHoldingCommandHandler.cs
using OneOf;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

/// <summary>Opens a position, or merges the purchase into the one that already exists.</summary>
public sealed class AddHoldingCommandHandler(IHoldingRepository holdings, TimeProvider clock)
    : ICommandHandler<AddHoldingCommand, OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>>
{
    public async Task<OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>> Handle(
        AddHoldingCommand command,
        CancellationToken ct)
    {
        // Phase 2 validates shape only. Phase 3 swaps this for a provider lookup.
        if (!Ticker.Create(command.Ticker).TryPickT0(out var ticker, out _))
        {
            return new UnknownTicker(command.Ticker);
        }

        var price = Money.Usd(command.Price);

        // "Do I already hold this?" is a context question, so the handler asks it — it does not read
        // a SQLSTATE back out of an exception. Two truly simultaneous requests can both pass here;
        // the unique index is the real guarantee and the loser surfaces as 500. See §2.6.
        var existing = await holdings.FindAsync(command.UserId, ticker, ct);

        if (existing is not null)
        {
            if (!existing.Merge(command.Quantity, price, clock).TryPickT0(out _, out var mergeFailed))
            {
                return mergeFailed;
            }

            await holdings.UpdateAsync(existing, ct);

            return new HoldingMerged(HoldingSummary.From(existing));
        }

        if (!Holding.Create(command.UserId, ticker, command.Quantity, price, clock)
                .TryPickT0(out var created, out var createFailed))
        {
            return createFailed;
        }

        await holdings.AddAsync(created, ct);

        return new HoldingCreated(HoldingSummary.From(created));
    }
}
```

- [ ] **Step 3: The other three**

```csharp
// Holdings/Commands/UpdateHolding/UpdateHoldingCommand.cs
namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.UpdateHolding;

/// <summary>Corrects a mistyped position. Replaces quantity and price; never averages.</summary>
public sealed record UpdateHoldingCommand(Guid UserId, Guid HoldingId, decimal Quantity, decimal Price);
```

```csharp
// Holdings/Commands/UpdateHolding/UpdateHoldingCommandHandler.cs
using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.UpdateHolding;

/// <summary>Rewrites a position to what the user meant to type.</summary>
public sealed class UpdateHoldingCommandHandler(IHoldingRepository holdings, TimeProvider clock)
    : ICommandHandler<UpdateHoldingCommand, OneOf<HoldingSummary, NotFound, InvalidInput>>
{
    public async Task<OneOf<HoldingSummary, NotFound, InvalidInput>> Handle(
        UpdateHoldingCommand command,
        CancellationToken ct)
    {
        // Scoped to the user by the repository, so another user's id is NotFound and never Forbidden.
        var holding = await holdings.FindByIdAsync(command.UserId, new HoldingId(command.HoldingId), ct);

        if (holding is null)
        {
            return new NotFound();
        }

        if (!holding.Correct(command.Quantity, Money.Usd(command.Price), clock)
                .TryPickT0(out _, out var invalid))
        {
            return invalid;
        }

        await holdings.UpdateAsync(holding, ct);

        return HoldingSummary.From(holding);
    }
}
```

```csharp
// Holdings/Commands/RemoveHolding/RemoveHoldingCommand.cs
namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.RemoveHolding;

/// <summary>Closes a position.</summary>
public sealed record RemoveHoldingCommand(Guid UserId, Guid HoldingId);
```

```csharp
// Holdings/Commands/RemoveHolding/RemoveHoldingCommandHandler.cs
using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.RemoveHolding;

/// <summary>Closes a position and raises the one event anything downstream cares about.</summary>
public sealed class RemoveHoldingCommandHandler(IHoldingRepository holdings)
    : ICommandHandler<RemoveHoldingCommand, OneOf<Success, NotFound>>
{
    public async Task<OneOf<Success, NotFound>> Handle(RemoveHoldingCommand command, CancellationToken ct)
    {
        var holding = await holdings.FindByIdAsync(command.UserId, new HoldingId(command.HoldingId), ct);

        if (holding is null)
        {
            return new NotFound();
        }

        // Raised before the delete: the interceptor collects from the tracked entity during the save,
        // and a Deleted entity is Detached by the time the save has finished.
        holding.Remove();

        await holdings.RemoveAsync(holding, ct);

        return new Success();
    }
}
```

```csharp
// Holdings/Queries/GetHoldings/GetHoldingsQuery.cs
namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Queries.GetHoldings;

/// <summary>Every position this user holds.</summary>
public sealed record GetHoldingsQuery(Guid UserId);
```

```csharp
// Holdings/Queries/GetHoldings/GetHoldingsQueryHandler.cs
using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Queries.GetHoldings;

/// <summary>Lists a user's positions.</summary>
public sealed class GetHoldingsQueryHandler(IHoldingRepository holdings)
    : IQueryHandler<GetHoldingsQuery, IReadOnlyList<HoldingSummary>>
{
    public async Task<IReadOnlyList<HoldingSummary>> Handle(GetHoldingsQuery query, CancellationToken ct)
    {
        var owned = await holdings.ListAsync(query.UserId, ct);

        return [.. owned.Select(HoldingSummary.From)];
    }
}
```

A query that changes nothing returns its result type directly — no union, no `NotFound`. An empty portfolio is
an empty list, not an absent one.

⚠️ There is **no `GetHoldingsResult`.** The convention reserves `<UseCase>Result` for a success payload unique
to one use case; this one is shared, so it is `HoldingSummary` at the Application root.

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: clean. `Portfolio.Application` still references only `Domain`, `Contracts`, `OneOf` and
`Logging.Abstractions` — no EF, no Npgsql, no ASP.NET Core.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Application
git commit -m "Four use cases; add returns two successes because the merge is the interesting one"
```

---

### Task 12: The two contracts reads

**Files:**
- Create: `.../Portfolio.Infrastructure/Persistence/HoldingQueries.cs`

**Interfaces:**
- Produces: `internal sealed class HoldingQueries : ITickersHeldByAnyUser, IUsersHoldingTicker`.
- Consumed by: Phase 3's host adapter, Phase 4's alert evaluation (inside Portfolio), and Task 15's integration test.

- [ ] **Step 1: Write it**

```csharp
// .../Portfolio.Infrastructure/Persistence/HoldingQueries.cs
using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

/// <summary>The set-based reads other modules need, projected to primitives at the boundary.</summary>
internal sealed class HoldingQueries(PortfolioDbContext context) : ITickersHeldByAnyUser, IUsersHoldingTicker
{
    /// <inheritdoc/>
    public async Task<List<string>> GetAsync(CancellationToken ct)
        => await context.Holdings
            .AsNoTracking()
            .Select(h => h.Ticker.Value)
            .Distinct()
            .ToListAsync(ct);

    /// <inheritdoc/>
    public async Task<List<Guid>> GetAsync(string ticker, CancellationToken ct)
    {
        // Named `ticker` because phase-3's UsersHoldingTicker_GeneratedSql_UsesParameterPlaceholder asserts
        // the command text contains @__ticker_0 — EF derives that name from this closure variable.
        var parsed = new Ticker(ticker);

        return await context.Holdings
            .AsNoTracking()
            .Where(h => h.Ticker == parsed)
            .Select(h => h.UserId)
            .Distinct()
            .ToListAsync(ct);
    }
}
```

Three things this settles for Phase 3 and Phase 4:

- **`AsNoTracking()` is correct here and only here.** These are read models that nothing mutates. Task 9's
  repository must never gain it, for the reason stated there.
- **No visibility filter on either.** Phase 5's `is_visible` is a *display* filter; a hidden position is still
  polled and still alerts. `phase-3` has a test named `PollSet_IncludesHiddenHoldings` for exactly this.
- **`Distinct()` runs in the database**, not in memory — two users holding AAPL is one held-ticker entry.

⚠️ `new Ticker(ticker)` bypasses `Ticker.Create`, so an unnormalised argument silently matches nothing. The
callers are a host adapter and the alert evaluator, both of which pass a value that came out of `GetAsync` and is
already canonical. Documented rather than defended, because re-validating on a hot read is the guard-in-the-
constructor trap again.

- [ ] **Step 2: Build, then commit**

```bash
dotnet build && git add src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/Persistence/HoldingQueries.cs && git commit -m "The two reads phases 3 and 4 will need, returning primitives"
```

---

### Task 13: The module seam — and the Migrator line the spec says is unnecessary

**Files:**
- Create: `.../Portfolio.Infrastructure/PortfolioModule.cs`
- Create: `.../Portfolio.Infrastructure/DependencyInjection.cs`
- Modify: `src/Migrator/Program.cs`

**Interfaces:**
- Produces: `PortfolioModule.AddPortfolioModule(IServiceCollection, IConfiguration)` — the module's entire
  public surface to the host, and the only public type in `.Infrastructure`.

- [ ] **Step 1: The seam**

```csharp
// .../Portfolio.Infrastructure/PortfolioModule.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;
using StockPortfolio.Shared.Kernel.DomainEvents;

namespace StockPortfolio.Modules.Portfolio.Infrastructure;

/// <summary>The Portfolio module's entire public surface to the host.</summary>
public static class PortfolioModule
{
    /// <summary>The ConnectionStrings key this module reads.</summary>
    public const string ConnectionStringName = "Portfolio";

    /// <summary>Registers the Portfolio module: its DbContext, event dispatch, repository, queries, handlers.</summary>
    public static IServiceCollection AddPortfolioModule(
        this IServiceCollection services,
        IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        var connectionString = config.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set "
                + $"ConnectionStrings:{ConnectionStringName} (or ConnectionStrings__{ConnectionStringName}). "
                + "Passing a null connection string to UseNpgsql throws later, from a stack that names "
                + "neither the key nor the file.");
        }

        // ⛔ The two registrations and the AddInterceptors call are WITHDRAWN by §0.0, with Task 10.
        // Register the context with UseNpgsql and the history table only; there is no interceptor,
        // so `(sp, options)` collapses to `options`.
        services.AddScoped<IDomainEventPublisher, DomainEventPublisher>();
        services.AddScoped<DispatchDomainEventsInterceptor>();

        services.AddDbContext<PortfolioDbContext>((sp, options) => options
            .UseNpgsql(
                connectionString,
                npg => npg.MigrationsHistoryTable(
                    PortfolioDbContext.MigrationsHistoryTableName,
                    PortfolioDbContext.SchemaName))
            .AddInterceptors(sp.GetRequiredService<DispatchDomainEventsInterceptor>()));

        services.AddScoped<IHoldingRepository, HoldingRepository>();

        // One instance, two contracts: the same reads, and neither consumer sees the other's.
        services.AddScoped<HoldingQueries>();
        services.AddScoped<ITickersHeldByAnyUser>(sp => sp.GetRequiredService<HoldingQueries>());
        services.AddScoped<IUsersHoldingTicker>(sp => sp.GetRequiredService<HoldingQueries>());

        services.AddPortfolioHandlers();

        return services;
    }
}
```

⛔ ~~**The interceptor and publisher are registered *here*, not in `Program.cs`.** `src/Migrator/Program.cs`
builds a bare `new ServiceCollection()` and calls only each module's `Add…Module`. Register them in the host
instead and the migrator throws on `GetRequiredService<DispatchDomainEventsInterceptor>()` before applying a
single migration.~~ Withdrawn by §0.0 with Task 10. The general point survives and is worth remembering: a
module's own `Add<M>Module` must register everything its `DbContext` needs, because the Migrator builds a bare
`ServiceCollection` and calls nothing else.

⚠️ **Eager validation is limited to the connection string.** `CLAUDE.md`'s "Where Identity is not a safe
template" warns that Identity validates *all* config eagerly and that this breaks Phase 3, where a missing
`Finnhub__ApiKey` is a supported state. Portfolio genuinely cannot run without a database, so this one guard
is right — and the migrator satisfies it, because `Migrator/Program.cs` already overrides
`ConnectionStrings:Portfolio` with the migrator connection string.

- [ ] **Step 2: Handler registrations**

```csharp
// .../Portfolio.Infrastructure/DependencyInjection.cs
using Microsoft.Extensions.DependencyInjection;

using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Portfolio.Application;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.RemoveHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.UpdateHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Queries.GetHoldings;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Infrastructure;

/// <summary>Handler registrations, kept out of PortfolioModule so the public seam stays one method.</summary>
internal static class DependencyInjection
{
    internal static IServiceCollection AddPortfolioHandlers(this IServiceCollection services)
    {
        services.AddScoped<
            ICommandHandler<AddHoldingCommand, OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>>,
            AddHoldingCommandHandler>();

        services.AddScoped<
            ICommandHandler<UpdateHoldingCommand, OneOf<HoldingSummary, NotFound, InvalidInput>>,
            UpdateHoldingCommandHandler>();

        services.AddScoped<
            ICommandHandler<RemoveHoldingCommand, OneOf<Success, NotFound>>,
            RemoveHoldingCommandHandler>();

        services.AddScoped<
            IQueryHandler<GetHoldingsQuery, IReadOnlyList<HoldingSummary>>,
            GetHoldingsQueryHandler>();

        return services;
    }
}
```

The closed generics are spelled out, which is the cost of returning `OneOf<…>` directly — and the reason a
missing case is a compile error at every call site rather than a runtime surprise.

- [ ] **Step 3: THE LINE THE SPEC SAYS IS UNNECESSARY**

`src/Migrator/Program.cs`, at the comment that already reads `// One line per module.`:

```csharp
services.AddIdentityModule(migratorConfiguration);
services.AddPortfolioModule(migratorConfiguration);
```

plus `using StockPortfolio.Modules.Portfolio.Infrastructure;` at the top.

⚠️ **This is the P0 blocker.** `phase-2-my-portfolio.md` §4 claims the migration job picks the new context up
automatically "because it runs a bundle over all four contexts". It is not a bundle — it discovers contexts by
scanning `ServiceDescriptor`s for `IsSubclassOf(typeof(DbContext))`, and only registered modules appear.
`docs/deferred-work.md` C8 already names the failure: *"With one module the `Count == 0` guard catches it
loudly; with two it does not, and that module's migrations are silently skipped."* Miss this line and
`docker compose up` succeeds, the migrator logs "IdentityDbContext is up to date", exits 0, and every
holdings request 500s against a schema with no tables.

Related: register with `AddDbContext<T>`, never `AddDbContextFactory<T>` — the discovery scan only recognises
the former.

- [ ] **Step 4: Prove the migrator sees both contexts**

```bash
docker compose up -d postgres
dotnet run --project src/Migrator -- --ConnectionStrings:Migrator="Host=localhost;Port=5432;Database=stockportfolio;Username=migrator;Password=migrator_dev_only"
```

Expected output names **both**:

```
migrator: IdentityDbContext is up to date.
migrator: PortfolioDbContext applying 1 migration(s): 20260804xxxxxx_InitialPortfolio
migrator: PortfolioDbContext done.
migrator: complete, 2 context(s) checked.
```

If it says `1 context(s) checked`, step 3 did not happen.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/PortfolioModule.cs src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Infrastructure/DependencyInjection.cs src/Migrator/Program.cs
git commit -m "Wire the module, and tell the migrator it exists"
```

---

### Task 14: `Portfolio.Api` — requests, validators, endpoints

**Files:**
- Create: `.../Portfolio.Api/Requests/{AddHoldingRequest,UpdateHoldingRequest}.cs`
- Create: `.../Portfolio.Api/Validators/{AddHoldingRequestValidator,UpdateHoldingRequestValidator}.cs`
- Create: `.../Portfolio.Api/PortfolioEndpoints.cs`
- Modify: `src/Shared.Api/ProblemDetailsExtensions.cs`
- Create: `tests/StockPortfolio.Modules.Portfolio.UnitTests/RequestValidatorTests.cs`

- [ ] **Step 1: `Shared.Api` needs a 404 helper**

It currently has exactly three: `ToValidationProblem` (400), `ConflictProblem` (409), `UnauthorizedProblem`
(401). Three of Phase 2's four routes can answer 404.

```csharp
    /// <summary>Builds a 404 problem response.</summary>
    public static ProblemHttpResult NotFoundProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status404NotFound, title: "Not Found");
```

- [ ] **Step 2: Requests and validators**

```csharp
// .../Portfolio.Api/Requests/AddHoldingRequest.cs
namespace StockPortfolio.Modules.Portfolio.Api.Requests;

/// <summary>A purchase to record. The user is taken from the bearer token, never from here.</summary>
public sealed record AddHoldingRequest(string Ticker, decimal Quantity, decimal Price);
```

```csharp
// .../Portfolio.Api/Requests/UpdateHoldingRequest.cs
namespace StockPortfolio.Modules.Portfolio.Api.Requests;

/// <summary>A correction to an existing position. Replaces the values; it is not a second purchase.</summary>
public sealed record UpdateHoldingRequest(decimal Quantity, decimal Price);
```

`Price` is `decimal` and arrives as a plain JSON number. `NumberHandling.Strict` rejects a *quoted* number for
a bare `decimal`; an ordinary one binds fine (§2.5). The route id supplies the holding id, so
`UpdateHoldingRequest` does not carry one — a body id that disagreed with the route id would be a bug with two
possible readings.

```csharp
// .../Portfolio.Api/Validators/AddHoldingRequestValidator.cs
using FluentValidation;

using StockPortfolio.Modules.Portfolio.Api.Requests;

namespace StockPortfolio.Modules.Portfolio.Api.Validators;

/// <summary>Shape only: is this even a ticker, a quantity, a price? No I/O.</summary>
public sealed class AddHoldingRequestValidator : AbstractValidator<AddHoldingRequest>
{
    /// <summary>One unit of numeric(18,6); below this a quantity rounds to zero on store.</summary>
    private const decimal MinimumQuantity = 0.000001m;

    public AddHoldingRequestValidator()
    {
        RuleFor(request => request.Ticker)
            .NotEmpty().WithMessage("A ticker is required.")
            .Matches("^[A-Za-z]{1,5}$").WithMessage("A ticker is 1 to 5 letters, A to Z.");

        RuleFor(request => request.Quantity)
            .GreaterThanOrEqualTo(MinimumQuantity)
            .WithMessage("Quantity must be at least 0.000001.");

        RuleFor(request => request.Price)
            .GreaterThan(0m).WithMessage("Purchase price must be greater than zero.");
    }
}
```

`UpdateHoldingRequestValidator` is the same two numeric rules without the ticker.

The regex accepts either case and `Ticker.Create` upper-cases — rejecting `aapl` at the boundary would be
rejecting a correct request for looking untidy.

⚠️ **This duplicates `Holding`'s guards, and that is the design, not an oversight.** Shape is checked in the
transport layer and returns 400 through `ValidationFilter<T>`; the invariant is checked in the entity and can
never be bypassed by a future non-HTTP caller. The validator is the friendly message; the entity is the
guarantee.

- [ ] **Step 3: Validator unit tests**

```csharp
// tests/StockPortfolio.Modules.Portfolio.UnitTests/RequestValidatorTests.cs
using Shouldly;
using StockPortfolio.Modules.Portfolio.Api.Requests;
using StockPortfolio.Modules.Portfolio.Api.Validators;

namespace StockPortfolio.Tests;

public sealed class RequestValidatorTests
{
    private static readonly AddHoldingRequestValidator Add = new();

    [Theory]
    [InlineData("AAPL", 10, 100)]
    [InlineData("aapl", 0.000001, 0.01)]
    [InlineData("F", 1, 1)]
    public void Add_AcceptsAWellFormedPurchase(string ticker, decimal quantity, decimal price) =>
        Add.Validate(new AddHoldingRequest(ticker, quantity, price)).IsValid.ShouldBeTrue();

    [Theory]
    [InlineData("TOOLONG", 10, 100, "Ticker")]
    [InlineData("", 10, 100, "Ticker")]
    [InlineData("BRK.B", 10, 100, "Ticker")]
    [InlineData("'; DROP TABLE portfolio.holdings; --", 10, 100, "Ticker")]
    [InlineData("AAPL", 0, 100, "Quantity")]
    [InlineData("AAPL", -1, 100, "Quantity")]
    [InlineData("AAPL", 0.0000001, 100, "Quantity")]
    [InlineData("AAPL", 10, 0, "Price")]
    [InlineData("AAPL", 10, -5, "Price")]
    public void Add_RejectsAndNamesTheField(string ticker, decimal quantity, decimal price, string field) =>
        Add.Validate(new AddHoldingRequest(ticker, quantity, price))
            .Errors.Select(error => error.PropertyName)
            .ShouldContain(field);
}
```

- [ ] **Step 4: The endpoints**

```csharp
// .../Portfolio.Api/PortfolioEndpoints.cs
using System.Security.Claims;

using FluentValidation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Portfolio.Api.Requests;
using StockPortfolio.Modules.Portfolio.Api.Validators;
using StockPortfolio.Modules.Portfolio.Application;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.RemoveHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.UpdateHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Queries.GetHoldings;
using StockPortfolio.Shared.Api;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Api;

/// <summary>The Portfolio module's entire inbound HTTP surface: four routes under /api/holdings.</summary>
public static class PortfolioEndpoints
{
    private const string BasePath = "/api/holdings";

    /// <summary>The claim carrying the user id.</summary>
    private const string SubjectClaimType = "sub";

    /// <summary>Registers the module's presentation-layer services: the request validators.</summary>
    public static IServiceCollection AddPortfolioApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<AddHoldingRequestValidator>();

        return services;
    }

    /// <summary>Maps the four holdings routes onto /api/holdings.</summary>
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        // Every route needs a bearer token and every route can 500, so both go on the group.
        var group = app.MapGroup(BasePath)
            .RequireAuthorization()
            .WithTags("Holdings")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/", GetAsync)
            .WithName("GetHoldings")
            .WithSummary("Lists every position the caller holds.")
            .Produces<IReadOnlyList<HoldingSummary>>(StatusCodes.Status200OK);

        group.MapPost("/", AddAsync)
            .AddEndpointFilter<ValidationFilter<AddHoldingRequest>>()
            .WithName("AddHolding")
            .WithSummary("Records a purchase, opening a position or merging into an existing one.")
            .WithDescription("201 when the position is new, 200 when the purchase merged into one you already held. Location points at the position either way.")
            .Produces<HoldingSummary>(StatusCodes.Status201Created)
            .Produces<HoldingSummary>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .AddEndpointFilter<ValidationFilter<UpdateHoldingRequest>>()
            .WithName("UpdateHolding")
            .WithSummary("Corrects a mistyped position.")
            .WithDescription("Replaces quantity and price. This is not a purchase, so nothing is averaged.")
            .Produces<HoldingSummary>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapDelete("/{id:guid}", RemoveAsync)
            .WithName("RemoveHolding")
            .WithSummary("Closes a position.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Lists the caller's positions.</summary>
    private static async Task<IResult> GetAsync(
        ClaimsPrincipal principal,
        IQueryHandler<GetHoldingsQuery, IReadOnlyList<HoldingSummary>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        return TypedResults.Ok(await handler.Handle(new GetHoldingsQuery(userId), ct));
    }

    /// <summary>Records a purchase.</summary>
    private static async Task<IResult> AddAsync(
        AddHoldingRequest request,
        ClaimsPrincipal principal,
        ICommandHandler<AddHoldingCommand, OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(
            new AddHoldingCommand(userId, request.Ticker, request.Quantity, request.Price),
            ct);

        return result.Match<IResult>(
            // 201 with a Location, because this position did not exist a moment ago.
            created => TypedResults.Created($"{BasePath}/{created.Holding.Id}", created.Holding),

            // 200, because the position already existed and this purchase changed it.
            merged => TypedResults.Ok(merged.Holding),

            invalid => invalid.ToValidationProblem(),

            unknownTicker => new InvalidInput("ticker", $"'{unknownTicker.Ticker}' is not a ticker this application recognises.")
                .ToValidationProblem());
    }

    /// <summary>Corrects a position.</summary>
    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateHoldingRequest request,
        ClaimsPrincipal principal,
        ICommandHandler<UpdateHoldingCommand, OneOf<HoldingSummary, NotFound, InvalidInput>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(
            new UpdateHoldingCommand(userId, id, request.Quantity, request.Price),
            ct);

        return result.Match<IResult>(
            corrected => TypedResults.Ok(corrected),

            // 404 and not 403: a 403 would confirm to a stranger that this id exists.
            missing => ProblemDetailsExtensions.NotFoundProblem("No such position."),

            invalid => invalid.ToValidationProblem());
    }

    /// <summary>Closes a position.</summary>
    private static async Task<IResult> RemoveAsync(
        Guid id,
        ClaimsPrincipal principal,
        ICommandHandler<RemoveHoldingCommand, OneOf<Success, NotFound>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(new RemoveHoldingCommand(userId, id), ct);

        return result.Match<IResult>(
            closed => TypedResults.NoContent(),
            missing => ProblemDetailsExtensions.NotFoundProblem("No such position."));
    }

    /// <summary>Reads the subject claim. Totality over a string?, not a security control.</summary>
    private static bool TryReadUserId(ClaimsPrincipal principal, out Guid userId, out IResult rejection)
    {
        // OnTokenValidated already rejects a subject-less token; this only gives null a branch.
        if (Guid.TryParse(principal.FindFirstValue(SubjectClaimType), out userId))
        {
            rejection = TypedResults.Empty;
            return true;
        }

        rejection = ProblemDetailsExtensions.UnauthorizedProblem("The access token carries no usable subject.");
        return false;
    }
}
```

⚠️ **`Location` goes on the 201 only.** Spec §2.4 says to set it on both. A `Location` on a 200 is not
*wrong*, but nothing reads it and expressing it means abandoning `TypedResults.Ok` for a hand-built result.
The 201 carries it; the README says so.

⚠️ **`UnknownTicker` is mapped to a 400 by re-wrapping it as an `InvalidInput`**, so the client sees a
field-level error on `ticker` exactly as it would from the filter. When Phase 3 turns this into a real symbol
lookup, that mapping is the line to revisit — a symbol the provider has never heard of may deserve its own
status and certainly deserves its own message.

Both `.Produces<HoldingSummary>(201)` and `(200)` are declared on `POST` — that is the honest encoding of
create-or-merge and is exactly what Task 16's metadata theory checks.

- [ ] **Step 5: Run the validator tests, then commit**

```bash
dotnet test tests/StockPortfolio.Modules.Portfolio.UnitTests --filter RequestValidatorTests
git add src/Modules/Portfolio/StockPortfolio.Modules.Portfolio.Api src/Shared.Api/ProblemDetailsExtensions.cs tests/StockPortfolio.Modules.Portfolio.UnitTests/RequestValidatorTests.cs
git commit -m "Four routes, every status declared, 404 where a 403 would leak"
```

---

### Task 15: Host wiring

Three lines, and their **position matters**.

**Files:**
- Modify: `src/Api/Program.cs`
- Modify: `src/Api/StockPortfolio.Api.http`

- [ ] **Step 1: Register the module above `DecorateHandlers()`**

`src/Api/Program.cs`, section 4:

```csharp
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddIdentityApi();

builder.Services.AddPortfolioModule(builder.Configuration);
builder.Services.AddPortfolioApi();

// Must come AFTER the modules: a decorator only applies to descriptors that already exist.
builder.Services.DecorateHandlers();
```

⚠️ **Above `DecorateHandlers()`, not below it.** `DecorateExtensions` uses Scrutor's `Decorate` over the open
generics `ICommandHandler<,>` and `IQueryHandler<,>`, which only wraps descriptors that are already
registered. Append the two Portfolio lines after it and every Portfolio handler silently loses logging —
compiles, registers, passes every test, logs nothing.

Then the map call, beside `app.MapIdentityEndpoints()`:

```csharp
app.MapIdentityEndpoints();
app.MapPortfolioEndpoints();
```

⚠️ `docs/deferred-work.md` C11: a missed `Map` builds, registers, passes every test, and serves nothing. Task
17's `EndpointDataSource_ExposesTheFourHoldingsRoutes` is the test that makes that impossible.

Add both usings: `StockPortfolio.Modules.Portfolio.Infrastructure;` and
`StockPortfolio.Modules.Portfolio.Api;`.

`src/Api/StockPortfolio.Api.csproj` needs **no** change — it already ProjectReferences every module's
`.Infrastructure` and `.Api`. (It referenced four modules when this was written; the Alerts references went
with the module in §0.0.)

- [ ] **Step 2: Extend the `.http` file**

Append to `src/Api/StockPortfolio.Api.http`, after the auth block, reusing its `@accessToken` variable:

```http
### List holdings
GET {{host}}/api/holdings
Authorization: Bearer {{accessToken}}

### Add AAPL 10 @ $100 — expect 201
POST {{host}}/api/holdings
Authorization: Bearer {{accessToken}}
Content-Type: application/json

{ "ticker": "AAPL", "quantity": 10, "price": 100 }

### Add AAPL 10 @ $150 — expect 200, quantity 20, average 125
POST {{host}}/api/holdings
Authorization: Bearer {{accessToken}}
Content-Type: application/json

{ "ticker": "aapl", "quantity": 10, "price": 150 }

### Correct it to 15 @ $120 — expect 200
PATCH {{host}}/api/holdings/{{holdingId}}
Authorization: Bearer {{accessToken}}
Content-Type: application/json

{ "quantity": 15, "price": 120 }

### Close it — expect 204
DELETE {{host}}/api/holdings/{{holdingId}}
Authorization: Bearer {{accessToken}}
```

- [ ] **Step 3: Run it for real**

```bash
docker compose up -d postgres redis
dotnet run --project src/Api
```

Register through the `.http` file, then run the four holdings calls in order. Confirm by eye:
- the second POST returns **200**, `quantity` `20`, `averagePrice` `{"amount":"125","currency":"USD"}`
- `averagePrice` and `invested` are **JSON strings**, not numbers — that is Task 2 working
- the lower-case `"aapl"` merged into the same row

- [ ] **Step 4: Read the contract back from the document, not the source**

```bash
curl -s http://localhost:8080/openapi/v1.json | jq '.paths["/api/holdings"], .components.schemas | keys'
```

Confirm `AddHoldingRequest` appears and `AddHoldingCommand` does not. An `.Application` type in the OpenAPI
document means something bound off the wire that should not have.

- [ ] **Step 5: Commit**

```bash
git add src/Api/Program.cs src/Api/StockPortfolio.Api.http
git commit -m "Map the module — above DecorateHandlers, or the handlers log nothing"
```

---

### Task 16: Integration-test infrastructure

Spec §4 says this is "already in the fixture". The *container* is; the *wiring* is not.

**Files:**
- Modify: `tests/StockPortfolio.Api.IntegrationTests/Infrastructure/ApiFixture.cs`
- Modify: `tests/StockPortfolio.Api.IntegrationTests/Infrastructure/ModuleDbContextInterceptors.cs`
- Modify: `tests/StockPortfolio.Api.IntegrationTests/Infrastructure/Wire.cs`
- Modify: `tests/StockPortfolio.Api.IntegrationTests/MigrationTests.cs`

- [ ] **Step 1: Teach the fixture about the second connection string**

`SettingsFor` currently takes `(string identity, string redis)`. It needs Portfolio, and the signature change
ripples to `CreateHostWithClock` and `CreateHostWithUnreachableDependencies`, both of which pass one
connection string positionally. Change it to take the fixture's own two strings:

```csharp
    private static Dictionary<string, string?> SettingsFor(string identity, string portfolio, string redis) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Identity"] = identity,
            ["ConnectionStrings:Portfolio"] = portfolio,
            ["ConnectionStrings:Redis"] = redis,
            ["Jwt:SigningKey"] = SigningKey,
            ["Jwt:Issuer"] = "StockPortfolio",
            ["Jwt:Audience"] = "StockPortfolio",
            ["Cors:Origins:0"] = CorsOrigin,
        };
```

Every call site passes `PortfolioConnectionString` — which **already exists** on the fixture, because
`SchemaIsolationTests` uses it to prove `portfolio_svc` cannot read `identity`. For
`CreateHostWithUnreachableDependencies`, pass the same unreachable string for both.

- [ ] **Step 2: Migrate both contexts**

`ApplyMigrationsAsync` resolves one context by type. It becomes a loop:

```csharp
    /// <summary>Applies every module's migrations as migrator, in the order the Migrator itself uses.</summary>
    private async Task ApplyMigrationsAsync()
    {
        await using var migratorHost = new ApiFactory(
            SettingsFor(MigratorConnectionString, MigratorConnectionString, _redis.GetConnectionString()));

        using var scope = migratorHost.Services.CreateScope();

        foreach (var contextType in ModuleDbContextInterceptors.ModuleDbContextTypes())
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            await context.Database.MigrateAsync();
        }
    }
```

⚠️ **Both** connection strings are the migrator's here — the migrating host must own both schemas, and
`portfolio_svc` has no DDL rights.

- [ ] **Step 3: Extend `ModuleDbContextInterceptors`**

It currently exposes `AddToIdentity` and `IdentityDbContextType()`, the latter throwing unless exactly one
non-abstract `DbContext` exists in the Identity assembly. Add the Portfolio equivalents and one enumerator,
keeping the single-context assertion per module:

```csharp
    /// <summary>Attaches the recording interceptor to the Portfolio context.</summary>
    public static void AddToPortfolio(IServiceCollection services, RecordingDbCommandInterceptor recorder) =>
        AddTo(services, PortfolioDbContextType(), recorder);

    /// <summary>The Portfolio module's only DbContext.</summary>
    public static Type PortfolioDbContextType() =>
        SingleDbContextIn("StockPortfolio.Modules.Portfolio.Infrastructure");

    /// <summary>Every module context, in the order migrations must be applied.</summary>
    public static IEnumerable<Type> ModuleDbContextTypes()
    {
        yield return IdentityDbContextType();
        yield return PortfolioDbContextType();
    }
```

Then, in `InitializeAsync`, attach the recorder to **both**:

```csharp
        _api = new ApiFactory(
            SettingsFor(IdentityConnectionString, PortfolioConnectionString, _redis.GetConnectionString()),
            services =>
            {
                ModuleDbContextInterceptors.AddToIdentity(services, RecordedCommands);
                ModuleDbContextInterceptors.AddToPortfolio(services, RecordedCommands);
            });
```

⚠️ **This is the entire remaining work for spec §5's SQL-injection group.** `ParameterisationTests` already
holds `Queries_NeverInlineUserInput_IntoCommandText` (the recording-interceptor proof) and
`HostileInput_IsStoredVerbatim_AndExecutesNothing` (which *is* the proposed
`Register_EmailContainingQuoteAndComment_RoundTripsExactly`). Once the recorder is attached to the Portfolio
context, every holdings query in the suite is covered by the existing assembly-wide assertion. Do not rebuild
any of it.

- [ ] **Step 4: Fix the migration test the new schema breaks**

`MigrationTests.Migrations_ApplyCleanly_OnEmptyDatabase` asserts `historySchemas.ShouldBe(["identity"])`. A
second history table in `portfolio` **fails it** — which is the test doing its job. Update:

```csharp
        historySchemas.ShouldBe(["identity", "portfolio"]);
        historySchemas.ShouldNotContain("public");
```

and add the Portfolio half beside the Identity assertions:

```csharp
        var portfolioTables = await ReadStringsAsync(
            connection,
            """
            SELECT table_name FROM information_schema.tables
             WHERE table_schema = 'portfolio'
             ORDER BY table_name
            """);

        portfolioTables.ShouldContain("holdings");
        portfolioTables.ShouldContain("__EFMigrationsHistory");

        var portfolioApplied = await ReadStringsAsync(
            connection,
            """SELECT "MigrationId" FROM portfolio."__EFMigrationsHistory" ORDER BY "MigrationId" """);

        portfolioApplied.ShouldContain(id => id.EndsWith("InitialPortfolio", StringComparison.Ordinal));
```

⚠️ `historySchemas.ShouldNotContain("public")` is the load-bearing line. `HasDefaultSchema` does **not** move
`__EFMigrationsHistory` (efcore#24127, closed *not planned*); without `MigrationsHistoryTable(name, schema)`
per context, both contexts share `public.__EFMigrationsHistory`, each sees the other's migration ids as
applied, and it looks exactly like data corruption. With two contexts this test finally has something to say.

- [ ] **Step 5: Add holdings helpers to `Wire`**

```csharp
    /// <summary>The body of a holdings response. Money arrives as a string, per MoneyJsonConverter.</summary>
    public sealed record MoneyPayload(string Amount, string Currency);

    /// <summary>One position as the API returns it.</summary>
    public sealed record HoldingPayload(
        Guid Id, string Ticker, decimal Quantity, MoneyPayload AveragePrice,
        MoneyPayload Invested, bool IsVisible, DateTimeOffset UpdatedAt);

    /// <summary>Posts a purchase to /api/holdings.</summary>
    public static Task<HttpResponseMessage> AddHoldingAsync(
        HttpClient client, string accessToken, string ticker, decimal quantity, decimal price) =>
        SendAsync(client, HttpMethod.Post, "/api/holdings", accessToken, new { ticker, quantity, price });

    /// <summary>Reads /api/holdings.</summary>
    public static async Task<IReadOnlyList<HoldingPayload>> ListHoldingsAsync(
        HttpClient client, string accessToken)
    {
        using var response = await SendAsync(client, HttpMethod.Get, "/api/holdings", accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Describe(response));

        return (await response.Content.ReadFromJsonAsync<List<HoldingPayload>>(JsonSerializerOptions.Web))!;
    }
```

Note `MoneyPayload.Amount` is a `string`. Typing it as `decimal` would fail to deserialise — and that failure
is itself worth having, because it proves the converter is active.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test`
Expected: green, with the architecture assembly **still red** on `EmptyShells_AreExactlyThePhasesNotYetBuilt`.
Task 17 closes that deliberately.

- [ ] **Step 7: Commit**

```bash
git add tests/StockPortfolio.Api.IntegrationTests/Infrastructure tests/StockPortfolio.Api.IntegrationTests/MigrationTests.cs
git commit -m "Teach the fixture about the second schema, and let the history-table test finally mean something"
```

---

### Task 17: The architecture lists that must move by hand

Two hard-coded lists exist precisely so this is a deliberate edit rather than a silent drift.

**Files:**
- Modify: `tests/StockPortfolio.Architecture.Tests/ModuleBoundaryTests.cs`

> ⚠️ **Amended by §0.0.** The arithmetic below predates the Alerts merge, which removed five shell assemblies
> from the list on its own. Do not copy the numbers — read the list in
> `EmptyShells_AreExactlyThePhasesNotYetBuilt` and delete the five Portfolio entries from whatever is actually
> there. After both changes the list is the five MarketData assemblies plus
> `StockPortfolio.Modules.Identity.Contracts`.

- [ ] **Step 1: Shrink the empty-shell list from 16 to 11**

Delete these five entries from `EmptyShells_AreExactlyThePhasesNotYetBuilt`'s `expected` array:

```
"StockPortfolio.Modules.Portfolio.Api",
"StockPortfolio.Modules.Portfolio.Application",
"StockPortfolio.Modules.Portfolio.Contracts",
"StockPortfolio.Modules.Portfolio.Domain",
"StockPortfolio.Modules.Portfolio.Infrastructure",
```

leaving ~~eleven: the five Alerts, the five MarketData,~~ **six: the five MarketData assemblies** and
`StockPortfolio.Modules.Identity.Contracts` — which stays empty on purpose, and whose own README says why.
The five Alerts assemblies were deleted outright by §0.0.

- [ ] **Step 2: Add Portfolio to the populated list**

In `PopulatedAssemblies_AreNotEmptyShells_SoTheRulesAreNotAllSkipped`:

```csharp
            SolutionAssemblies.NameOf("Portfolio", "Contracts"),
            SolutionAssemblies.NameOf("Portfolio", "Domain"),
            SolutionAssemblies.NameOf("Portfolio", "Application"),
            SolutionAssemblies.NameOf("Portfolio", "Infrastructure"),
            SolutionAssemblies.NameOf("Portfolio", "Api"),
```

Note this list gains **five** where Identity has four: `Portfolio.Contracts` carries code (§2.8) and
`Identity.Contracts` does not.

- [ ] **Step 3: Run the whole architecture suite and read the skip count**

Run: `dotnet test tests/StockPortfolio.Architecture.Tests`

Expected: green, and **the skip count falls**. Every rule that was skipping for Portfolio now runs:
rule 1 (cross-module), rule 2 (Contracts has no persistence — for the first time on any module),
rule 3 (no public setter), rule 4 (Infrastructure has no ASP.NET Core), rule 5 (Api has neither EF nor its own
Infrastructure).

⚠️ **Quote the skipped count alongside the passing count.** `CLAUDE.md` pins 20 skips against 188 passing; both
numbers move here, and a passing count quoted alone hides a rule that stopped asserting.

- [ ] **Step 4: Break one rule on purpose, then put it back**

A rule that has never been seen red is not enforcement. Temporarily add to
`StockPortfolio.Modules.Portfolio.Api.csproj`:

```xml
    <ProjectReference Include="..\StockPortfolio.Modules.Portfolio.Infrastructure\StockPortfolio.Modules.Portfolio.Infrastructure.csproj" />
```

Run: `dotnet build && dotnet test tests/StockPortfolio.Architecture.Tests --filter ApiAssembly_ReferencesNeitherPersistenceNorItsOwnInfrastructure`
Expected: **FAIL**, naming the path `…Portfolio.Api -> …Portfolio.Infrastructure`. Then revert the line and
confirm green. This is how the `AssembliesFor("Infrastructure")` copy-paste was found.

- [ ] **Step 5: Commit**

```bash
git add tests/StockPortfolio.Architecture.Tests/ModuleBoundaryTests.cs
git commit -m "Portfolio is no longer a shell, so five rules stop skipping"
```

---

### Task 18: Integration tests

**Files:**
- Create: `tests/StockPortfolio.Api.IntegrationTests/HoldingsTests.cs`
- Modify: `tests/StockPortfolio.Api.IntegrationTests/EndpointMetadataTests.cs`

- [ ] **Step 1: The holdings suite**

```csharp
// tests/StockPortfolio.Api.IntegrationTests/HoldingsTests.cs
using System.Net;

using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Api.IntegrationTests.Infrastructure;
using StockPortfolio.Modules.Portfolio.Contracts;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>Portfolio CRUD end to end, over real HTTP against a real Postgres.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class HoldingsTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    private async Task<(HttpClient Client, string Token)> SignedInAsync(string prefix)
    {
        var client = _fixture.CreateClient();
        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix));

        return (client, tokens.AccessToken);
    }

    [Fact]
    public async Task AddHolding_ReturnsCreated_WithLocationHeader()
    {
        var (client, token) = await SignedInAsync("holdings-create");

        using var response = await Wire.AddHoldingAsync(client, token, "AAPL", 10m, 100m);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await Wire.Describe(response));
        response.Headers.Location!.ToString().ShouldStartWith("/api/holdings/");
    }

    // The canonical case, end to end.
    [Fact]
    public async Task AddHolding_SameTickerTwice_Returns200Merged_OneRowInDatabase()
    {
        var (client, token) = await SignedInAsync("holdings-merge");

        using var first = await Wire.AddHoldingAsync(client, token, "AAPL", 10m, 100m);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        // Lower case on purpose: Ticker.Create normalises, so this must find the same row.
        using var second = await Wire.AddHoldingAsync(client, token, "aapl", 10m, 150m);
        second.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(second));

        var holdings = await Wire.ListHoldingsAsync(client, token);

        var only = holdings.ShouldHaveSingleItem();
        only.Ticker.ShouldBe("AAPL");
        only.Quantity.ShouldBe(20m);

        // Parsed, not string-compared: decimal preserves scale, so the serialised form is legitimately
        // either "125" or "125.000000" depending on the scale division happened to produce. Asserting
        // the string would be asserting an implementation detail of decimal arithmetic.
        Money(only.AveragePrice).ShouldBe(125m);
        Money(only.Invested).ShouldBe(2500m);
    }

    /// <summary>Parses the string form back to a decimal. That it IS a string is asserted separately.</summary>
    private static decimal Money(Wire.MoneyPayload payload) =>
        decimal.Parse(payload.Amount, System.Globalization.CultureInfo.InvariantCulture);

    // The string form itself is the contract, so assert it directly and once.
    [Fact]
    public async Task Holdings_SerialiseMoneyAsAString_NotANumber()
    {
        var (client, token) = await SignedInAsync("holdings-money-shape");

        await Wire.AddHoldingAsync(client, token, "IBM", 2m, 50m);

        using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/holdings", token);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        body.ShouldContain("\"amount\":\"", Case.Sensitive,
            "MoneyJsonConverter must emit a quoted amount; an unquoted one means it is not registered.");
    }

    [Theory]
    [InlineData("TOOLONG")]
    [InlineData("BRK.B")]
    [InlineData("")]
    public async Task AddHolding_MalformedTicker_Returns400(string ticker)
    {
        var (client, token) = await SignedInAsync("holdings-bad-ticker");

        using var response = await Wire.AddHoldingAsync(client, token, ticker, 10m, 100m);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await Wire.Describe(response));
        response.Content.Headers.ContentType!.MediaType.ShouldBe(Wire.ProblemJson);
    }

    [Fact]
    public async Task UpdateHolding_ChangesQuantityAndPrice_AndDoesNotAverage()
    {
        var (client, token) = await SignedInAsync("holdings-update");

        await Wire.AddHoldingAsync(client, token, "MSFT", 10m, 100m);
        await Wire.AddHoldingAsync(client, token, "MSFT", 10m, 150m);          // now 20 @ 125

        var id = (await Wire.ListHoldingsAsync(client, token)).Single().Id;

        using var response = await Wire.SendAsync(
            client, HttpMethod.Patch, $"/api/holdings/{id}", token, new { quantity = 15m, price = 120m });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var corrected = (await Wire.ListHoldingsAsync(client, token)).ShouldHaveSingleItem();
        corrected.Quantity.ShouldBe(15m);
        corrected.AveragePrice.Amount.ShouldBe("120", "Correct replaces; it must never average");
    }

    // A 403 would confirm the id exists. This is the security assertion of the phase.
    [Fact]
    public async Task UpdateHolding_OtherUsersHolding_Returns404_NotForbidden()
    {
        var (ownerClient, ownerToken) = await SignedInAsync("holdings-owner");
        await Wire.AddHoldingAsync(ownerClient, ownerToken, "TSLA", 5m, 200m);
        var id = (await Wire.ListHoldingsAsync(ownerClient, ownerToken)).Single().Id;

        var (strangerClient, strangerToken) = await SignedInAsync("holdings-stranger");

        using var response = await Wire.SendAsync(
            strangerClient, HttpMethod.Patch, $"/api/holdings/{id}", strangerToken,
            new { quantity = 1m, price = 1m });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await Wire.Describe(response));
    }

    [Fact]
    public async Task RemoveHolding_Returns204_ThenGetIsEmpty()
    {
        var (client, token) = await SignedInAsync("holdings-remove");

        await Wire.AddHoldingAsync(client, token, "NVDA", 3m, 400m);
        var id = (await Wire.ListHoldingsAsync(client, token)).Single().Id;

        using var removed = await Wire.SendAsync(client, HttpMethod.Delete, $"/api/holdings/{id}", token);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await Wire.ListHoldingsAsync(client, token)).ShouldBeEmpty();
    }

    [Fact]
    public async Task RemoveHolding_Twice_SecondReturns404()
    {
        var (client, token) = await SignedInAsync("holdings-remove-twice");

        await Wire.AddHoldingAsync(client, token, "AMD", 2m, 90m);
        var id = (await Wire.ListHoldingsAsync(client, token)).Single().Id;

        using var first = await Wire.SendAsync(client, HttpMethod.Delete, $"/api/holdings/{id}", token);
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var second = await Wire.SendAsync(client, HttpMethod.Delete, $"/api/holdings/{id}", token);
        second.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetHoldings_ShowsOnlyTheCallersPositions()
    {
        var (aliceClient, aliceToken) = await SignedInAsync("holdings-alice");
        await Wire.AddHoldingAsync(aliceClient, aliceToken, "AAPL", 1m, 100m);

        var (bobClient, bobToken) = await SignedInAsync("holdings-bob");

        (await Wire.ListHoldingsAsync(bobClient, bobToken)).ShouldBeEmpty();
    }

    // The unique index is the guarantee, not the handler's lookup. See §2.6.
    [Fact]
    public async Task AddHolding_ConcurrentSameTicker_OneRowSurvives()
    {
        var (client, token) = await SignedInAsync("holdings-race");

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => Wire.AddHoldingAsync(client, token, "AAPL", 10m, 100m)));

        try
        {
            responses.ShouldContain(
                response => response.IsSuccessStatusCode,
                "at least one of four concurrent purchases must land");

            (await Wire.ListHoldingsAsync(client, token))
                .Count(holding => string.Equals(holding.Ticker, "AAPL", StringComparison.Ordinal))
                .ShouldBe(1, "ix_holdings_user_id_ticker is what makes a duplicate row impossible");
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    // Proves the Portfolio half of the seam Phase 3's host adapter sits on. No event needed.
    [Fact]
    public async Task NewHolding_AppearsInThePollSet_Immediately()
    {
        var (client, token) = await SignedInAsync("holdings-pollset");

        await Wire.AddHoldingAsync(client, token, "GOOG", 1m, 100m);

        using var scope = _fixture.Services.CreateScope();
        var pollSet = scope.ServiceProvider.GetRequiredService<ITickersHeldByAnyUser>();

        (await pollSet.GetAsync(TestContext.Current.CancellationToken)).ShouldContain("GOOG");
    }

    [Fact]
    public async Task Holders_OfATicker_AreTheUsersWhoHoldIt()
    {
        var (client, token) = await SignedInAsync("holdings-holders");
        await Wire.AddHoldingAsync(client, token, "META", 1m, 100m);

        using var scope = _fixture.Services.CreateScope();
        var holders = scope.ServiceProvider.GetRequiredService<IUsersHoldingTicker>();

        (await holders.GetAsync("META", TestContext.Current.CancellationToken)).ShouldNotBeEmpty();
    }
}
```

⚠️ **`AddHolding_ConcurrentSameTicker_OneRowSurvives` deliberately does not assert the loser's status.** Under
§2.6 the loser is a 500, and pinning that would be pinning an accident rather than a decision. What matters,
and what the unique index actually guarantees, is that exactly one row exists. Name the mutation that turns it
red: drop `.IsUnique()` from `HoldingConfiguration` and this test goes red while every other test stays green.

⚠️ **Do not add SQL-injection tests here.** `ParameterisationTests` covers them assembly-wide, and Task 16
extended its interceptor to the Portfolio context. `AddHolding_MalformedTicker_Returns400` with a `BRK.B`
payload is the defence-in-depth half; `'; DROP TABLE …` is already covered by `TickerTests` and
`RequestValidatorTests` at the unit level, where it costs nothing.

- [ ] **Step 2: Extend the endpoint-metadata theory**

`EndpointMetadataTests` hard-codes `AuthRouteNames` and an 11-row status matrix. Add the Portfolio half —
this is the only thing standing between `.Produces(...)` and silent drift, and it matters more here because
`POST` declares both 201 and 200.

Add a second route-name array, a second smoke-detector fact, and these rows to a Portfolio theory:

| Route | Scenario | Status |
|---|---|---|
| `GetHoldings` | `bearer` | 200 |
| `GetHoldings` | `anonymous` | 401 |
| `AddHolding` | `fresh` | 201 |
| `AddHolding` | `duplicate-ticker` | **200** |
| `AddHolding` | `bad-ticker` | 400 |
| `UpdateHolding` | `own` | 200 |
| `UpdateHolding` | `stranger` | 404 |
| `RemoveHolding` | `own` | 204 |
| `RemoveHolding` | `missing` | 404 |

Reuse the existing shape exactly: drive the scenario over real HTTP, assert the status the theory expects,
then assert the endpoint's `IProducesResponseTypeMetadata` contains it. Reuse
`AuthRoute_ProblemStatuses_DeclareProblemJson` over the new names too — every 4xx these routes declare is
served as `problem+json`.

- [ ] **Step 3: Run everything**

Run: `dotnet test`
Expected: green. Record both numbers — passing **and** skipped — in the commit message.

- [ ] **Step 4: Commit**

```bash
git add tests/StockPortfolio.Api.IntegrationTests
git commit -m "Portfolio end to end: merge, correct, 404 for a stranger, one row under a race"
```

---

### Task 19: The holdings data layer in the SPA

**Files:**
- Create: `src/Web/src/portfolio/holdingsApi.ts`
- Create: `src/Web/src/portfolio/useHoldingMutations.ts`

`react-hook-form` 7.84.0, `zod` 4.4.3 and `@hookform/resolvers` 5.6.0 are **already installed as runtime
dependencies**. Phase 2 runs no `npm install`.

- [ ] **Step 1: Fetchers and keys, beside each other**

The repo convention puts query keys next to the fetchers for that feature — `authKeys` lives in
`src/Web/src/auth/authApi.ts`, not in `lib/`.

```ts
// src/Web/src/portfolio/holdingsApi.ts
import { queryOptions } from '@tanstack/react-query'
import { apiFetch } from '../lib/apiClient'

/** Money arrives as a string so nothing here parses it as a float. */
export interface Money {
  amount: string
  currency: string
}

export interface Holding {
  id: string
  ticker: string
  quantity: number
  averagePrice: Money
  invested: Money
  isVisible: boolean
  updatedAt: string
}

export interface AddHoldingBody {
  ticker: string
  quantity: number
  price: number
}

export interface UpdateHoldingBody {
  quantity: number
  price: number
}

export const holdingKeys = {
  all: ['holdings'] as const,
  list: () => [...holdingKeys.all, 'list'] as const,
}

export const holdingsQuery = queryOptions({
  queryKey: holdingKeys.list(),
  queryFn: () => apiFetch<Holding[]>('/api/holdings'),
})

/** Returns the row, plus whether it merged — the 200-vs-201 the UI announces. */
export async function addHolding(body: AddHoldingBody): Promise<{ holding: Holding; merged: boolean }> {
  const holding = await apiFetch<Holding>('/api/holdings', { method: 'POST', body })

  // The API distinguishes create from merge by status; apiFetch returns only the body, so the
  // quantity is what tells us. A merge is the only way quantity exceeds what we just sent.
  return { holding, merged: holding.quantity > body.quantity }
}

export const updateHolding = (id: string, body: UpdateHoldingBody): Promise<Holding> =>
  apiFetch<Holding>(`/api/holdings/${id}`, { method: 'PATCH', body })

export const removeHolding = (id: string): Promise<void> =>
  apiFetch<void>(`/api/holdings/${id}`, { method: 'DELETE' })
```

⚠️ **`addHolding` infers `merged` from the returned quantity, and that is a compromise worth naming.**
`apiFetch` returns the parsed body and discards the `Response`, so the 201-vs-200 distinction is not
reachable without changing `apiFetch`'s signature for every caller. Inferring from quantity is correct for
every real case — a merge always sums, so the returned quantity always exceeds the submitted one. If Phase 3
needs the status itself, add an `apiFetchWithStatus` rather than widening `apiFetch`.

- [ ] **Step 2: The three mutations, against the *current* callback signature**

Verified against the installed `@tanstack/react-query` 5.101.4 type definitions:

```
onMutate  (variables, context)
onSuccess (data, variables, onMutateResult, context)
onError   (error, variables, onMutateResult, context)
onSettled (data, error, variables, onMutateResult, context)
```

The `onMutate` snapshot is at **position 3** in `onError`/`onSuccess` and **position 4** in `onSettled` — the
same positions it occupied before v5.89. v5.89 *renamed* the generic `TContext` → `TOnMutateResult` and
*appended* a new `context`; it did not insert anything before the snapshot. `phase-2-my-portfolio.md` §3 and
`CLAUDE.md` both claim otherwise and both are wrong (Task 22 corrects them).

```ts
// src/Web/src/portfolio/useHoldingMutations.ts
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  addHolding,
  holdingKeys,
  removeHolding,
  updateHolding,
  type AddHoldingBody,
  type Holding,
  type UpdateHoldingBody,
} from './holdingsApi'

interface Snapshot {
  previous: Holding[] | undefined
}

/** Cancels in-flight reads and snapshots the list, so onError has something to restore. */
async function snapshot(client: ReturnType<typeof useQueryClient>): Promise<Snapshot> {
  await client.cancelQueries({ queryKey: holdingKeys.list() })
  return { previous: client.getQueryData<Holding[]>(holdingKeys.list()) }
}

export function useAddHolding() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (body: AddHoldingBody) => addHolding(body),

    // No optimistic row: the server decides the id and, on a merge, the new average.
    // Guessing either would show a number that is about to change.
    onSettled: () => queryClient.invalidateQueries({ queryKey: holdingKeys.list() }),
  })
}

export function useUpdateHolding() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateHoldingBody }) => updateHolding(id, body),

    onMutate: async ({ id, body }) => {
      const previous = await snapshot(queryClient)

      queryClient.setQueryData<Holding[]>(holdingKeys.list(), (old) =>
        (old ?? []).map((holding) =>
          holding.id === id
            ? { ...holding, quantity: body.quantity, averagePrice: { ...holding.averagePrice, amount: String(body.price) } }
            : holding,
        ),
      )

      return previous
    },

    // Position 3 is the onMutate result. It is `| undefined` because onMutate may never have run.
    onError: (_error, _variables, onMutateResult) => {
      if (onMutateResult?.previous) {
        queryClient.setQueryData(holdingKeys.list(), onMutateResult.previous)
      }
    },

    onSettled: () => queryClient.invalidateQueries({ queryKey: holdingKeys.list() }),
  })
}

export function useRemoveHolding() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (id: string) => removeHolding(id),

    onMutate: async (id) => {
      const previous = await snapshot(queryClient)

      queryClient.setQueryData<Holding[]>(holdingKeys.list(), (old) =>
        (old ?? []).filter((holding) => holding.id !== id),
      )

      return previous
    },

    onError: (_error, _id, onMutateResult) => {
      if (onMutateResult?.previous) {
        queryClient.setQueryData(holdingKeys.list(), onMutateResult.previous)
      }
    },

    onSettled: () => queryClient.invalidateQueries({ queryKey: holdingKeys.list() }),
  })
}
```

Three notes:

- **`cancelQueries` and `invalidateQueries` take a filters object; `getQueryData` and `setQueryData` take a
  positional key.** That split is the v5 rule: anything that can match many queries takes filters.
- **Add has no optimistic row on purpose.** The server assigns the id and, on a merge, recomputes the average.
  An optimistic row would flash a wrong average — which is the one number this phase exists to get right.
- **`onMutateResult?.previous` needs the `?.`** — `strictNullChecks` types it `Snapshot | undefined` in
  `onError`, and the repo's tsconfig would reject a bare access with TS18048.

- [ ] **Step 3: Typecheck, then commit**

```bash
npm --prefix src/Web run typecheck
git add src/Web/src/portfolio && git commit -m "Holdings fetchers, keys and optimistic mutations"
```

---

### Task 20: `Table` and `ConfirmDialog`

Neither exists. Spec §3's *"the mobile card fallback from Phase 1"* refers to something that was never built —
`Card.tsx` is a titled section panel, unrelated. Both are new.

**Files:**
- Create: `src/Web/src/components/Table.tsx`
- Create: `src/Web/src/components/ConfirmDialog.tsx`

Follow the established component conventions exactly: a named export per file, an exported
`export interface <Name>Props`, no default export, no barrel, explicit relative imports, and semantic Tailwind
tokens only — `bg-panel`, `border-bd`, `text-tx`, `text-mu`, `font-mono` for anything numeric. `props` are
written `error?: string | undefined` because `exactOptionalPropertyTypes` is false.

- [ ] **Step 1: `Table` — a real table on desktop, cards under 640px**

```tsx
// src/Web/src/components/Table.tsx
import type { ReactNode } from 'react'

export interface Column<TRow> {
  /** Stable key, also the mobile card's label. */
  header: string
  /** Cell contents. Kept a render function so money stays a formatted string. */
  cell: (row: TRow) => ReactNode
  /** Right-align and monospace — for quantities and money. */
  numeric?: boolean | undefined
}

export interface TableProps<TRow> {
  columns: Array<Column<TRow>>
  rows: TRow[]
  rowKey: (row: TRow) => string
  caption: string
  empty?: ReactNode | undefined
}

/**
 * One data set, two presentations. Below sm the <table> is hidden and the same rows render as
 * labelled cards — a horizontally scrolling table at 375px is unreadable, and the brief asks for
 * a usable mobile layout rather than a shrunken desktop one.
 */
export function Table<TRow>({ columns, rows, rowKey, caption, empty }: TableProps<TRow>) {
  if (rows.length === 0) {
    return <div className="text-mu px-1 py-6 text-[12.5px]">{empty ?? 'Nothing here yet.'}</div>
  }

  return (
    <>
      <table className="hidden w-full border-collapse text-[12.5px] sm:table">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr className="border-bd border-b">
            {columns.map((column) => (
              <th
                key={column.header}
                scope="col"
                className={`text-mu px-2 py-2 text-[11.5px] font-medium tracking-[0.04em] uppercase ${
                  column.numeric ? 'text-right' : 'text-left'
                }`}
              >
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={rowKey(row)} className="border-bd/60 border-b last:border-0">
              {columns.map((column) => (
                <td
                  key={column.header}
                  className={`px-2 py-2.5 ${column.numeric ? 'text-right font-mono' : 'text-tx'}`}
                >
                  {column.cell(row)}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>

      <ul className="flex flex-col gap-2.5 sm:hidden">
        {rows.map((row) => (
          <li key={rowKey(row)} className="border-bd bg-panel-2 flex flex-col gap-1.5 rounded-lg border p-3">
            {columns.map((column) => (
              <div key={column.header} className="flex items-baseline justify-between gap-3">
                <span className="text-mu text-[11.5px] tracking-[0.04em] uppercase">{column.header}</span>
                <span className={column.numeric ? 'font-mono text-[12.5px]' : 'text-tx text-[12.5px]'}>
                  {column.cell(row)}
                </span>
              </div>
            ))}
          </li>
        ))}
      </ul>
    </>
  )
}
```

`sr-only` comes from Tailwind's built-ins — `index.css` defines no such utility and `Spinner.tsx` already
relies on the built-in one.

- [ ] **Step 2: `ConfirmDialog` — the one screen that needs real keyboard handling**

There is no `Dialog` primitive, no focus-trap helper and no portal, and **no UI library may be added** — the
brief bans component kits and the ban is a graded item. Hand-build it.

```tsx
// src/Web/src/components/ConfirmDialog.tsx
import { useEffect, useRef } from 'react'
import { Button } from './Button'

export interface ConfirmDialogProps {
  open: boolean
  title: string
  body: string
  confirmLabel: string
  onConfirm: () => void
  onCancel: () => void
  busy?: boolean | undefined
}

/**
 * A modal with a focus trap, Escape-to-close and aria-modal, built by hand because the brief bans
 * UI component libraries. Focus moves to Cancel on open — the safe action — and returns to whatever
 * opened the dialog on close, or the user is dropped at the top of the document.
 */
export function ConfirmDialog({
  open,
  title,
  body,
  confirmLabel,
  onConfirm,
  onCancel,
  busy,
}: ConfirmDialogProps) {
  const panelRef = useRef<HTMLDivElement>(null)
  const cancelRef = useRef<HTMLButtonElement>(null)
  const openerRef = useRef<HTMLElement | null>(null)

  useEffect(() => {
    if (!open) return

    openerRef.current = document.activeElement as HTMLElement | null
    cancelRef.current?.focus()

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        onCancel()
        return
      }

      if (event.key !== 'Tab') return

      // The trap. Without it, Tab walks out of the dialog into the page behind it, which is
      // still rendered and still focusable.
      const focusable = panelRef.current?.querySelectorAll<HTMLElement>('button:not([disabled])')
      if (!focusable || focusable.length === 0) return

      const first = focusable[0]!
      const last = focusable[focusable.length - 1]!

      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)

    return () => {
      document.removeEventListener('keydown', onKeyDown)
      openerRef.current?.focus()
    }
  }, [open, onCancel])

  if (!open) return null

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4"
      onClick={onCancel}
      role="presentation"
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="confirm-title"
        aria-describedby="confirm-body"
        className="border-bd bg-panel w-full max-w-sm rounded-xl border p-5"
        onClick={(event) => event.stopPropagation()}
      >
        <h2 id="confirm-title" className="text-tx text-[15px] font-semibold">
          {title}
        </h2>
        <p id="confirm-body" className="text-mu mt-2 text-[12.5px] leading-relaxed">
          {body}
        </p>

        <div className="mt-5 flex justify-end gap-2">
          <Button ref={cancelRef} type="button" onClick={onCancel}>
            Cancel
          </Button>
          <Button type="button" onClick={onConfirm} disabled={busy}>
            {busy ? 'Removing…' : confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  )
}
```

⚠️ `Button` must forward its ref for `cancelRef` to work. `TextField` already does; check `Button.tsx` and
wrap it in `forwardRef` if it does not — that is a one-line change to a Phase 1 file and the reason it is
called out here rather than discovered at runtime.

⚠️ **No `NumberField`.** Spec §3 asks for one; `TextField` already spreads `...rest`, so
`type="number"`, `step` and `inputMode` pass straight through. A wrapper adding nothing is a component to
maintain for no gain.

- [ ] **Step 3: Typecheck and commit**

```bash
npm --prefix src/Web run typecheck
git add src/Web/src/components && git commit -m "A table that becomes cards, and a dialog with a real focus trap"
```

---

### Task 21: The `/portfolio` route

**Files:**
- Create: `src/Web/src/routes/_authenticated/portfolio.tsx`
- Modify: `src/Web/src/components/AppShell.tsx`
- Modify: `src/Web/src/routeTree.gen.ts` (generated, but committed)

- [ ] **Step 1: The route**

The **first loader in the application**. `phase-3-live-prices.md` §3 cites this by name — *"Phase 2 used a
route `loader` for holdings. Quotes must not"* — so it is a deliberate contrast, not an incidental choice.

```tsx
// src/Web/src/routes/_authenticated/portfolio.tsx (abridged to the parts that carry decisions)
import { createFileRoute } from '@tanstack/react-router'
import { useSuspenseQuery } from '@tanstack/react-query'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { holdingsQuery, type Holding } from '../../portfolio/holdingsApi'
import { useAddHolding, useRemoveHolding } from '../../portfolio/useHoldingMutations'

export const Route = createFileRoute('/_authenticated/portfolio')({
  // queryClient comes from router context, typed once in __root.tsx — never the module singleton,
  // or the memory routers in tests stop working.
  loader: ({ context: { queryClient } }) => queryClient.ensureQueryData(holdingsQuery),
  component: PortfolioPage,
})

// zod v4: top-level z.email/z.string(), not the v3 z.string().email() chain. Messages are KEYS,
// so Phase 5's i18n can translate them without touching validation.
const addHoldingSchema = z.object({
  ticker: z.string().regex(/^[A-Za-z]{1,5}$/, 'errors.ticker.format'),
  quantity: z.coerce.number().positive('errors.quantity.positive'),
  price: z.coerce.number().positive('errors.price.positive'),
})

type AddHoldingForm = z.infer<typeof addHoldingSchema>
```

The component then:

1. reads the list with `useSuspenseQuery(holdingsQuery)` — the same options object the loader warmed, so
   there is no second fetch and no loading flash;
2. renders `<AppShell title="Portfolio">` with a `<Card title="Positions">` containing `<Table>` over the
   columns **Asset · Qty · Buy · ×**. No price and no P&L — those arrive in Phase 3;
3. renders the add-position form with `react-hook-form` + `zodResolver`, `noValidate` on the `<form>`, and
   `applyServerErrors(error, setError, ['ticker', 'quantity', 'price'])` in the catch, exactly as
   `login.tsx` does;
4. **disables the submit button while `isPending`** — this is the §2.6 defence: it removes the double-click
   that is the only realistic source of the merge race;
5. shows `<Alert tone="success">` when `addHolding` reports `merged`, reading
   *"Merged into your AAPL position — 20 shares, average $125.00."* This is the phase's demo moment; a silent
   row update hides the only interesting business rule in it;
6. computes **Invested** by summing `holding.invested.amount` — server-computed strings parsed only for
   display. Never `quantity * price` in the browser.

The two fragments that carry the decisions, in full:

```tsx
  const add = useAddHolding()
  const [merged, setMerged] = useState<Holding | null>(null)

  const onSubmit = handleSubmit(async (values) => {
    setFormError('')
    setMerged(null)

    try {
      const result = await add.mutateAsync(values)
      if (result.merged) setMerged(result.holding)
      reset()
    } catch (error) {
      setFormError(applyServerErrors(error, setError, ['ticker', 'quantity', 'price']))
    }
  })

  // The phase's demo moment. A silent row update hides the only interesting business rule here.
  {merged ? (
    <Alert tone="success">
      Merged into your {merged.ticker} position — {merged.quantity} shares, average{' '}
      {formatMoney(merged.averagePrice)}.
    </Alert>
  ) : null}

  // Disabled while pending: this is the §2.6 defence, removing the double-click that is the
  // only realistic way two identical purchases race each other.
  <Button type="submit" size="lg" disabled={add.isPending}>
    {add.isPending ? 'Adding…' : 'Add position'}
  </Button>
```

```tsx
/**
 * Invested is summed from the server's own per-row figures, never recomputed from quantity x price.
 * The server rounds the average to 6dp on store; multiplying a rounded average in float64 here
 * would disagree with it, and the totals row is exactly where such errors accumulate visibly.
 */
function totalInvested(holdings: Holding[]): string {
  const total = holdings.reduce((sum, holding) => sum + Number(holding.invested.amount), 0)

  return total.toLocaleString(undefined, { style: 'currency', currency: 'USD' })
}
```

⚠️ `Number(...)` on a money string is a float, and that is acceptable **only** for a display total. It must
never feed a value that is sent back to the server or compared against one. Phase 3's P&L figures are all
computed server-side for the same reason.

⚠️ `Button` sets `type="button"` and then spreads `...rest`, so the submit button **must** pass
`type="submit"` explicitly or the form does nothing. `login.tsx` and `register.tsx` both do.

⚠️ `Alert tone="success"` renders `role="status"`, which is polite. A test asserting on it must use
`getByRole('status')`, not `getByRole('alert')`.

- [ ] **Step 2: Add the nav entry**

`AppShell.tsx` has `const NAV: NavItem[] = [{ to: '/dashboard', label: 'Dashboard' }]` and a comment saying
Portfolio joins it in phase 2. Add `{ to: '/portfolio', label: 'Portfolio' }`.

⚠️ **The comment beside `NAV` claims a nav entry pointing at an unknown route is a type error. It is not.**
`NavItem.to` is declared `string`, and TanStack Router's `ToPathOption` short-circuits on
`string extends TTo ? string : …`, which disables the literal check entirely. Only inline literal `to="..."`
props are checked. Adding `/portfolio` before the route file exists compiles and 404s at runtime. Correct that
comment while you are in the file.

- [ ] **Step 3: Regenerate the route tree — and mind the ordering trap**

`routeTree.gen.ts` is committed and is regenerated only inside the router plugin's `vite.configResolved`
hook, which runs for `vite dev`, `vite build` and `vitest run` — but **not** for `tsc --noEmit`. Since
`npm run build` is literally `tsc --noEmit && vite build`, the typecheck runs first, against the stale
committed tree.

```bash
npm --prefix src/Web test
npm --prefix src/Web run typecheck
git diff --stat src/Web/src/routeTree.gen.ts
```

Run the tests (or `dev`) **once** before trusting `typecheck`, and commit the regenerated tree.

- [ ] **Step 4: See it in a browser**

```bash
docker compose up -d postgres redis && dotnet run --project src/Api &
npm --prefix src/Web run dev
```

Log in, click Portfolio, add AAPL 10 @ $100, then AAPL 10 @ $150. **One row, 20 shares, $125, merge notice
shown, Invested $2,500.** Then narrow the window to 375px and confirm the table becomes cards.

- [ ] **Step 5: Commit**

```bash
git add src/Web/src/routes src/Web/src/components/AppShell.tsx src/Web/src/routeTree.gen.ts
git commit -m "The portfolio screen: one row, a merge notice, and cards at 375px"
```

---

### Task 22: Frontend tests

**Files:**
- Create: `src/Web/tests/portfolio.test.tsx`

The current baseline is 4 files / 13 tests, all green, `tsc --noEmit` clean.

⚠️ Three fixture facts that will otherwise cost an hour:

- `tests/msw/server.ts` is `setupServer()` with **zero** handlers and `setup.ts` uses
  `onUnhandledRequest: 'error'`. Any test rendering `/portfolio` must register
  `server.use(http.get('*/api/holdings', …))` itself. The leading `*` matters — `API_BASE_URL` is `''` in tests.
- The `QueryClient` is a **module singleton shared across every test file in the run**. `queryClient.clear()`
  in `beforeEach`, or seeded holdings leak into the next file.
- There is **no shared render helper**. `auth.test.tsx` and `sessionPersistence.test.tsx` both duplicate the
  `createMemoryHistory` / `createRouter` / `render(<RouterProvider router={router as AnyRouter} />)`
  boilerplate inline. A third copy is the convention; extracting a helper would be a new pattern and is out
  of scope.

- [ ] **Step 1: Write the four tests spec §3 names**

```tsx
// src/Web/tests/portfolio.test.tsx
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { beforeEach, describe, expect, it } from 'vitest'
import { QueryClientProvider } from '@tanstack/react-query'
import { server } from './msw/server'
import { queryClient } from '../src/lib/queryClient'
import { authStore } from '../src/auth/authStore'
import { PortfolioPage } from '../src/routes/_authenticated/portfolio'

const AAPL = {
  id: '0199a1f0-0000-7000-8000-000000000001',
  ticker: 'AAPL',
  quantity: 10,
  averagePrice: { amount: '100', currency: 'USD' },
  invested: { amount: '1000', currency: 'USD' },
  isVisible: true,
  updatedAt: '2026-08-04T12:00:00+00:00',
}

// The query client is a module singleton shared by every test FILE in the run.
beforeEach(() => {
  queryClient.clear()
  authStore.signOut()
})

function renderPage(seed = [AAPL]) {
  queryClient.setQueryData(['holdings', 'list'], seed)

  return render(
    <QueryClientProvider client={queryClient}>
      <PortfolioPage />
    </QueryClientProvider>,
  )
}

describe('portfolio', () => {
  it('adds a position and shows it in the table', async () => {
    const added = { ...AAPL, id: '...0002', ticker: 'MSFT', quantity: 5,
      averagePrice: { amount: '300', currency: 'USD' },
      invested: { amount: '1500', currency: 'USD' } }

    server.use(
      http.post('*/api/holdings', () => HttpResponse.json(added, { status: 201 })),
      http.get('*/api/holdings', () => HttpResponse.json([AAPL, added])),
    )

    renderPage()

    await userEvent.type(screen.getByLabelText(/ticker/i), 'MSFT')
    await userEvent.type(screen.getByLabelText(/quantity/i), '5')
    await userEvent.type(screen.getByLabelText(/price/i), '300')
    await userEvent.click(screen.getByRole('button', { name: /add position/i }))

    expect(await screen.findByText('MSFT')).toBeInTheDocument()
  })

  it('shows the merge notice when the API reports a merged purchase', async () => {
    // quantity 20 came back for a submitted 10 — that is what addHolding reads as a merge.
    const merged = { ...AAPL, quantity: 20,
      averagePrice: { amount: '125', currency: 'USD' },
      invested: { amount: '2500', currency: 'USD' } }

    server.use(
      http.post('*/api/holdings', () => HttpResponse.json(merged, { status: 200 })),
      http.get('*/api/holdings', () => HttpResponse.json([merged])),
    )

    renderPage()

    await userEvent.type(screen.getByLabelText(/ticker/i), 'AAPL')
    await userEvent.type(screen.getByLabelText(/quantity/i), '10')
    await userEvent.type(screen.getByLabelText(/price/i), '150')
    await userEvent.click(screen.getByRole('button', { name: /add position/i }))

    // Alert tone="success" renders role="status" (polite), NOT role="alert".
    const notice = await screen.findByRole('status')
    expect(notice).toHaveTextContent(/merged/i)
    expect(notice).toHaveTextContent(/125/)
  })

  // THE ONE THAT EARNS ITS KEEP: it fails if the rollback reads the wrong callback parameter.
  it('restores the row when an optimistic delete fails', async () => {
    server.use(
      http.delete('*/api/holdings/:id', () => new HttpResponse(null, { status: 500 })),
      http.get('*/api/holdings', () => HttpResponse.json([AAPL])),
    )

    renderPage()

    expect(screen.getByText('AAPL')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /remove aapl/i }))
    await userEvent.click(screen.getByRole('button', { name: /^remove$/i }))   // confirm dialog

    // Both states asserted. Checking only the end state would pass even if the optimistic
    // update never happened at all.
    await waitFor(() => expect(screen.queryByText('AAPL')).not.toBeInTheDocument())
    await waitFor(() => expect(screen.getByText('AAPL')).toBeInTheDocument())
  })

  it('rejects a 6-character ticker before submitting', async () => {
    let posts = 0
    server.use(http.post('*/api/holdings', () => { posts += 1; return HttpResponse.json(AAPL, { status: 201 }) }))

    renderPage()

    await userEvent.type(screen.getByLabelText(/ticker/i), 'TOOLONG')
    await userEvent.type(screen.getByLabelText(/quantity/i), '1')
    await userEvent.type(screen.getByLabelText(/price/i), '1')
    await userEvent.click(screen.getByRole('button', { name: /add position/i }))

    // A request counter, not just a visible message: asserting the message alone would pass
    // even if the request had ALSO been sent. Mirrors refreshDedupe.test.ts.
    await screen.findByText(/errors\.ticker\.format|ticker/i)
    expect(posts).toBe(0)
  })
})
```

⚠️ This imports `PortfolioPage` directly rather than mounting a memory router, because all four assertions
are about the component, not about routing. `portfolio.tsx` must therefore `export function PortfolioPage`
as well as its `Route`. If a routing assertion is ever needed, copy the
`createMemoryHistory` / `createRouter` / `render(<RouterProvider router={router as AnyRouter} />)` block
from `auth.test.tsx` — a third inline copy is the established convention here.

⚠️ Every handler URL starts with `*` because `API_BASE_URL` is `''` in tests, and `setup.ts` runs MSW with
`onUnhandledRequest: 'error'` — an unregistered call fails the test rather than hanging.

- [ ] **Step 2: Run, and check the count moved**

Run: `npm --prefix src/Web test`
Expected: 5 files, 17 tests, green.

- [ ] **Step 3: Commit**

```bash
git add src/Web/tests/portfolio.test.tsx
git commit -m "Four SPA tests, including the optimistic rollback that pins the v5 signature"
```

---

### Task 23: Correct the documents Phase 2 disproved

A plan file that disagrees with the code is worse than no plan file — the next reader follows the wrong rule.

> ⚠️ **Amended by §0.0.** The Alerts merge landed mid-phase and its documentation sweep was done separately,
> covering `CLAUDE.md`, `README.md`, `00-overview.md`, `module-interactions.md`, `er-diagram.md`,
> `deferred-work.md`, this file, and phases 1 and 3–6. Steps 1–4 below are the *original* Phase 2 sweep and
> are still owed; two of their items changed:
>
> - **Step 1 item 2** (the "Where Identity is not a safe template" commit-point row) is already done, and was
>   rewritten a second time: the row's driver is gone entirely, since there are no domain events to dispatch.
> - **Step 3's §4.1 amendment** (`<M>.Domain → <M>.Contracts`) is **withdrawn** — that edge was only needed to
>   let `Holding` raise a contracts-shaped event. §4.1's table is correct as written.
>
> Still owed here: the TanStack Query trap rewrite, both `ComplexProperty`/`Money` trap items, the
> `phase-2-my-portfolio.md` corrections, the §3 package-list note, and the README's Portfolio section.

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/plan/phase-2-my-portfolio.md`
- Modify: `docs/plan/phase-1-implementation.md`
- Modify: `README.md`

- [ ] **Step 1: `CLAUDE.md` — four edits**

1. **Delete the TanStack Query trap outright, or rewrite it.** It currently reads *"v5.89.0 changed every
   mutation callback signature (`onMutateResult` inserted before a new `context`). Every optimistic-update
   tutorial written before Sept 2025 rolls back the wrong snapshot."* The second sentence is **false**,
   verified three ways against the installed 5.101.4: by reading the shipped `.d.ts`, by reading the compiled
   `mutation.js` call sites, and by a compile test with a negative control. v5.89 renamed the generic and
   appended `context`; the snapshot stayed at position 3 / 4. Replace with:

   > **TanStack Query v5.89.0 renamed the mutation callbacks' `TContext` generic to `TOnMutateResult` and
   > appended a new `context` (`{ client, meta, mutationKey }`) as the last parameter of each; `mutationFn`
   > gained a second argument.** Positions did not move — the `onMutate` snapshot is still argument 3 in
   > `onError`/`onSuccess` and 4 in `onSettled` — so pre-5.89 rollback code still compiles and still restores
   > the correct value. The pinned version is 5.101.4.

   Leaving it as-is is not neutral: it would drive a pointless rewrite of working code.

2. **Update the "Where Identity is not a safe template" commit-point row.** It says Phase 2's
   dispatch-before-save requires handler writes in one transaction. §2.7 reversed that: dispatch is
   after-save, so Portfolio's repositories self-commit exactly like Identity's, and the row's warning no
   longer applies to Portfolio. Restate it as the general question — *"state the commit point on the
   repository interface's doc comment before the first handler exists"* — which is still right and is what
   Task 9 did.

3. **Extend the `ComplexProperty` trap.** Add that the failure is at **model build / host startup**, not
   first query; that `efcore#31621` was milestoned `11.0.0` in Feb 2026 and pushed back to `Backlog` with a
   `blocked` label in Jun 2026; and that `Money`'s get-only properties are **not mapped by convention**, so
   the `ComplexProperty` lambda must map each member explicitly.

4. **Add one new trap:** *"EF binds a complex type's own constructor for materialisation, exactly as it does
   an entity's. `Money(decimal, string)` calls `currency.ToUpperInvariant()`, so that runs on every row of
   every `SELECT`. The guard-free-constructor rule applies to value objects, not just entities."*

Also refresh the counts in the Tests section once Task 24 has the final numbers, and add
`Portfolio.UnitTests` to the assembly list.

- [ ] **Step 2: `phase-2-my-portfolio.md` — carry §0's eleven corrections back**

Do not rewrite the file. Follow the convention `docs/plan/` already uses: where a decision was reversed, say
so and why, rather than quietly showing the new shape. Minimum edits:

- §2.1 — `UserId` → `Guid`, with the one-line reason
- §2.2 — a note that Phase 2 shipped after-save dispatch, pointing at `phase-2-implementation.md` §2.7
- §2.3 — `HoldingSummary` is the one name; `HoldingDto` deleted
- §2.4 — `Location` on the 201 only
- §4 — "Infrastructure delta: **None**" is wrong; three files needed editing
- §5 — the two orphaned table rows (lines 248-249) given a header, one moved to Phase 3
- §6 — the `23505` line replaced with §2.6's decision, and the unsourced "EF 11 is deprecating owned-JSON"
  claim deleted
- §7 — record the three chosen values (6dp `ToEven`, price > 0, quantity ≥ 0.000001)

- [ ] **Step 3: `phase-1-implementation.md` §3 and §4.1**

Add a one-line note above the §3 package list saying it is a snapshot from 2026-08-02 and that
`Directory.Packages.props` is the source of truth — five versions have moved since. In §4.1's reference
table, add `<M>.Contracts` to `<M>.Domain`'s row, with §2.8's reason.

- [ ] **Step 4: README**

Add a Portfolio section covering:
- **the weighted-average rule with the worked example** — 10 @ $100 then 10 @ $150 → 20 @ $125 — and the
  rounding policy (6dp, banker's, on store)
- **why Update replaces rather than averages**: correcting a typo is not a second purchase, and conflating
  them behind one flag is how a fix silently becomes a buy
- **why `POST` answers 201 or 200**, and that `Location` is set on the 201
- **money on the wire**: a string out, a number in, and why
- the merge race and its accepted 500 (§2.6)

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md docs/plan README.md
git commit -m "Correct the four documents Phase 2 proved wrong, starting with a trap that was not real"
```

---

### Task 24: The phase is done when it runs, not when tests pass

- [ ] **Step 1: Clean-clone compose run**

```bash
git clean -xdf && docker compose down -v && docker compose up
```

Then, in a browser at `http://localhost:8080`, walk spec §8 exactly:

- [ ] log in → `/portfolio`
- [ ] add AAPL 10 @ $100 → row appears, Invested **$1,000**
- [ ] add AAPL 10 @ $150 → **still one row**, 20 shares, average **$125**, Invested **$2,500**, merge notice shown
- [ ] edit to 15 @ $120 → 15 shares @ $120, Invested **$1,800**
- [ ] delete → table empty, Invested $0
- [ ] reload → still empty (it actually persisted)
- [ ] add as user A, log in as user B → B sees nothing
- [ ] 375px wide → the table is cards

⚠️ Watch the compose logs for `migrator: complete, 2 context(s) checked.` If it says `1`, Task 13 step 3 is
missing and everything above will have failed anyway.

- [ ] **Step 2: Full test run, both numbers quoted**

```bash
dotnet build && dotnet test
npm --prefix src/Web test && npm --prefix src/Web run typecheck
```

Record passing **and** skipped. Both baselines moved with §0.0 — deleting five Alerts assemblies removed
architecture cases, and withdrawing Task 10 removed six unit tests — so compare against a freshly measured
pre-task run, not against the 188/29 figures this plan was written with. Expected direction is unchanged:
passing up, **skips down**, because five Portfolio assemblies stopped being shells. A skip count that did not
fall means Task 17 did not land.

⚠️ `dotnet test --no-build` after a **failed** build silently runs the previous assemblies and reports green.
Check the build result, not just the test result.

- [ ] **Step 3: Deploy and verify on the public URL**

```bash
az deployment group what-if -g stockportfolio-rg -f infra/main.bicep
```

Expected: **no changes**. Phase 2 adds no infrastructure — that is the point of front-loading it, and a
non-empty what-if means something leaked in.

Push, let `deploy.yml` run, then repeat the §8 walkthrough against the GitHub Pages URL talking to the ACA
API. Confirm the ACA migration job log names `PortfolioDbContext`.

`minReplicas: 0` stays for one more phase — there is still no `BackgroundService`, `IHostedService` or
`PeriodicTimer` anywhere in `src/`. **Phase 3 must set it back to 1.**

- [ ] **Step 4: Final commit**

```bash
git commit --allow-empty -m "Phase 2 verified: compose, browser, and the deployed URL"
```

---

## 6. Work order

Task numbers are **not** renumbered after §0.0's withdrawals — execution is keyed to them.

| # | Task | Verified by |
|---|---|---|
| 1 | ⛔ ~~Domain-event types in `Shared.Kernel`~~ | **WITHDRAWN (§0.0)** — skip it |
| 2 | `MoneyJsonConverter` | 5 new tests; money is a string on the wire |
| 3 | `Portfolio.UnitTests` project | builds, discovered, 0 tests |
| 4 | `HoldingId`, `Ticker` | 12 tests; **architecture suite goes red here** |
| 5 | `Portfolio.Contracts` — the two reads only | rule 2 runs instead of skipping, for the first time on any module |
| 6 | `Holding`, without the event surface | rule 3 runs and passes |
| 7 | Context, converters, configuration, `appsettings.Development.json` | `dotnet build` clean |
| 8 | Migration + `EfModelTests` | generated SQL read by eye: schema, snake_case, `numeric(18,6)`, unique index |
| — | *half day* | |
| 9 | `IHoldingRepository` + implementation | commit point stated on the interface |
| 10 | ⛔ ~~Dispatch interceptor + publisher~~ | **WITHDRAWN (§0.0)** — skip it |
| 11 | The four use cases | `.Application` still has no EF, no Npgsql, no ASP.NET Core |
| 12 | `HoldingQueries` | builds |
| 13 | `PortfolioModule`, DI, **`Migrator/Program.cs`** | migrator prints `2 context(s) checked` |
| 14 | `Portfolio.Api` + `NotFoundProblem` | validator tests |
| 15 | `Program.cs`, `.http` | manual run: second POST returns 200 with average 125; `/openapi/v1.json` names the *request* |
| 16 | Fixture wiring, `MigrationTests` | `dotnet test` green except the architecture lists |
| 17 | The two hard-coded architecture lists | skip count **falls**; one rule broken on purpose and restored |
| 18 | `HoldingsTests`, endpoint metadata | `dotnet test` green |
| — | *one day* | |
| 19 | `holdingsApi`, mutations | `typecheck` clean |
| 20 | `Table`, `ConfirmDialog` | `typecheck` clean |
| 21 | `/portfolio` route, nav, route tree | **works in a browser**; cards at 375px |
| 22 | SPA tests | 17 tests green |
| 23 | Document corrections | — |
| 24 | Compose, full suite, deploy | the §8 walkthrough on the public URL |
| — | *0.75 days total* | |

Tasks 2–8 come first because they set the shape every later file copies, and because task 4 is the moment the
architecture rules switch on. Tasks 1 and 10 are withdrawn; the day estimate does not change materially,
since between them they were about ninety lines and six tests.

---

## 7. Risks and deviations, stated up front

**Phase 2's biggest deviation is §0.0: Alerts is merged into Portfolio and domain events are withdrawn.**
It reverses the module split that `docs/plan/` argues for throughout, and it is argued in full in
[00-overview.md](00-overview.md) §"Three modules, not four". The deferred consequence is real and is tracked
in [../deferred-work.md](../deferred-work.md): the `alerts` schema, the `alerts_svc` role and the Alerts
deployment variables are still in `db/init/`, `docker-compose.yml`, `infra/` and the workflows, because
`docker compose up` is the P0 gate and removing them was not verifiable in the environment that made the
change.

⛔ ~~**Phase 2 reverses two Phase 1-era decisions, deliberately.** Dispatch is after-save, not before (§2.7),
and `Portfolio.Domain` references `Portfolio.Contracts`, which §4.1's reference table does not list (§2.8).~~
Both were then withdrawn by §0.0 — there is no dispatch and no event, so `Portfolio.Domain` needs no reference
to `Portfolio.Contracts` and §4.1's table stands as written. The principle survives: a reversal that does not
update the document it reverses is how the next reader follows a rule the code does not obey.

**The merge race surfaces as a 500, and that is a chosen cost** (§2.6). The window is the few milliseconds
between the handler's lookup and its insert, for one user on one ticker. The frontend's disabled-while-pending
button removes the realistic source. If that 500 is ever actually observed in a log, the fix is a catch in
`.Infrastructure` translating `23505` — **not** the retry the spec describes, which as written never
terminates.

**`Money`'s constructor runs on every materialised row.** `currency.ToUpperInvariant()` allocates once per
holding per query. At this scale it is invisible; at Phase 3's dashboard join over every position it is worth
re-measuring, and the fix is to move normalisation out of the constructor into the factories.

**Mapping `Money` member by member means a member added later is silently unmapped.**
`AveragePrice_MapsEveryMemberOfMoney` in Task 8 is the whole defence, and it exists because the alternative —
changing `Money` to `init` — edits a Phase 1 file with a passing suite to save four lines.

**`Portfolio.Contracts` ships two interfaces with no cross-module consumer until Phase 3.** They are
implemented and integration-tested here, so they are not speculative, but they are the one piece of Phase 2
whose *purpose* is external. If Phase 3 discovers it needs a different shape, changing them is cheap precisely
because nothing else consumes them yet.

⛔ ~~**`Ticker` is declared three times across the codebase by Phase 4**, once per module that needs one
(§2.2). That looks like duplication and is the opposite: it is what lets three modules be pulled apart.~~
**Withdrawn by §0.0, and inverted:** the third declaration was going to be Alerts', and the fact that `Ticker`
would have meant the same thing there is what showed Alerts was not a separate bounded context. Two
declarations remain, Portfolio's and MarketData's, across a real subdomain boundary. The other half of the
paragraph still stands: `phase-3-live-prices.md` §2.5's `IReadOnlySet<Ticker>` in `MarketData.Contracts`
violates the records-of-primitives rule and must be resolved in Phase 3, not here.

**`is_visible` ships unused** (§2.9). A column with no reader is normally a smell; here it is one line against
an `ALTER TABLE` on a live Azure database mid-demo, and it is what makes `phase-5`'s "no migration data step"
claim true.

⛔ ~~**The sync `SavingChanges` throws.** Any synchronous `SaveChanges()` anywhere in Portfolio — Migrator,
seeding, design-time tooling, a test helper — now fails loudly.~~ Withdrawn with Task 10 (§0.0). There is no
interceptor, so nothing throws and synchronous saves are merely unused.

---

## 8. Phase 2 exit checklist

- [ ] `docker compose up` from a clean clone → log in → `/portfolio`
- [ ] Add AAPL 10 @ $100 → row appears, Invested $1,000
- [ ] Add AAPL 10 @ $150 → **one row**, 20 shares, average $125, Invested $2,500, **merge notice shown**
- [ ] Edit to 15 @ $120 → 15 @ $120, Invested $1,800
- [ ] Delete → empty; reload → still empty
- [ ] User A's position is invisible to user B, and B's `PATCH` of A's id returns **404, not 403**
- [ ] Compose logs show `migrator: complete, 2 context(s) checked.`
- [ ] `dotnet test` green — passing **and** skipped both quoted, and **the skip count fell** against a freshly
      measured baseline (the 29 this plan quotes predates §0.0)
- [ ] No `Shared.Kernel/DomainEvents/` directory, no `HoldingRemoved`, no dispatch interceptor anywhere (§0.0)
- [ ] The counts left as `TODO(count)` in `CLAUDE.md` are replaced with real measured values
- [ ] `npm test` green including the optimistic-rollback test
- [ ] One architecture rule broken on purpose and seen red, then restored
- [ ] `/openapi/v1.json` names `AddHoldingRequest`; no `.Application` type appears in it
- [ ] `averagePrice` and `invested` are JSON **strings** in a real response
- [ ] `az deployment group what-if` reports **no changes**
- [ ] Deployed, and the walkthrough passes on the GitHub Pages URL
- [ ] `CLAUDE.md`'s TanStack Query trap corrected — it currently states something untrue
- [ ] README carries the weighted-average worked example and why Update replaces rather than averages
- [ ] Table readable at 375px as cards
- [ ] No new NuGet package, no new npm package, no Bicep change

