# Phase 2 — My portfolio · 0.9 days

## 1. Goal

Add AAPL 10 @ $100, then AAPL 10 @ $150 → **one row, 20 shares @ $125**. Edit the quantity. Delete the position. All of it persisted, all of it live on Azure.

Covers P0 req 4.

`Initial.md:104` designs Create, Read and Delete — but the brief's heading is **CRUD**, and a rubric row labelled CRUD looks for edit. Editing a mistyped purchase price is not a partial disposal and drags in no transaction history, so Update is added here. About an hour.

---

## 2. Backend

### 2.1 `Holding` aggregate — `Portfolio.Domain/Holding.cs`

One row per `(UserId, Ticker)`. A unique index enforces it in the database, because a C# guard alone cannot survive two concurrent requests.

```csharp
public sealed class Holding
{
    // Every mapped value EXCEPT AveragePrice, which is a complex type and cannot be a constructor
    // parameter (efcore#31621) - the factory assigns it after construction. See §6.
    private Holding(
        HoldingId id, Guid userId, Ticker ticker, decimal quantity,
        DateTimeOffset createdAt, DateTimeOffset updatedAt);

    public HoldingId Id { get; private set; }
    public Guid UserId { get; private set; }
    public Ticker Ticker { get; private set; }
    public decimal Quantity { get; private set; }
    public Money AveragePrice { get; private set; }      // ComplexProperty → two columns
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static OneOf<Holding, InvalidInput> Create(
        Guid userId, Ticker ticker, decimal quantity, Money purchasePrice, TimeProvider clock);

    /// Merges a new purchase into this position: quantities sum, price becomes the
    /// weighted average. 10 @ $100 then 10 @ $150 → 20 @ $125.
    public OneOf<Success, InvalidInput> Merge(decimal quantity, Money purchasePrice);

    /// Direct correction of a mistyped entry. Not a purchase — replaces, never averages.
    public OneOf<Success, InvalidInput> Correct(decimal quantity, Money purchasePrice);
}
```

✏️ **Corrected: `UserId` was `Identity.Domain`'s `UserId` here and is now a plain `Guid`.** Portfolio may reference only `Identity.Contracts`, which is empty, and `ModuleBoundaryTests` enforces it — so a module referencing a user it does not own stores a raw `Guid`. See `phase-2-implementation.md` §2.1.

No base class — see `phase-1-implementation.md` §5.2. `Holding` declares its own `Id`, has exactly one private all-args constructor that only assigns, and no settable surface, so `Create` is the only way in.

`Merge` and `Correct` are deliberately separate operations. Overloading one method with a flag is how a "fix my typo" silently becomes a second purchase.

⛔ ~~Deleting raises `HoldingRemoved(UserId, Ticker)`, consumed by Alerts in Phase 4 to clear any pending cooldown for that user and ticker. `Create` and `Merge` raise nothing.~~ **Withdrawn.** Alerts is a feature area inside Portfolio, not a module, so Phase 4 clears the cooldown with a call at the end of `RemoveHoldingCommandHandler`. `Holding` raises nothing and has no event surface. See `phase-2-implementation.md` §0.0.

⛔ ~~That asymmetry is deliberate and worth understanding, because an earlier draft had events on both. The held-ticker list is read live from Portfolio at the start of every cycle (Phase 3 §2.5), so *adding* a holding needs no notification — the next cycle simply sees it. Only removal has an effect nothing else would notice: a cooldown key sitting in Redis for a position you no longer own. One event, one consumer, one real job.~~

⛔ ~~The dispatch machinery below is still worth building for that single consumer — it is about forty lines, it is the seam every later cross-module event goes through, and Phase 4's `HoldingRemoved_ClearsCooldown` test exercises it end to end.~~

✏️ **Withdrawn with the event above.** Both paragraphs argued for building the dispatch seam anyway, and that argument dies with its only raiser: an abstraction with no raiser is what Phase 1 already wrote and deleted. **Do not build the forty lines.** Removal is `HoldingRepository.RemoveAsync` and nothing more. Phase 3's poller no longer reads the held-ticker list from Portfolio either — it polls tickers with an active alert, which Alerts owns.

`Ticker` is a `readonly record struct Ticker(string Value)` with a `ValueConverter`, normalised to uppercase, validated against `^[A-Z]{1,5}$` on construction.

### 2.2 Domain event dispatch — ⛔ WITHDRAWN

> **Nothing in this section is built.** `phase-2-implementation.md` §2.7 first reversed the dispatch point
> (after the save, not before), and §0.0 then withdrew domain events entirely: Alerts merged into Portfolio,
> `HoldingRemoved` lost its only reason to exist, and `Shared.Kernel/DomainEvents/` was deleted rather than
> written. There is no interceptor, no publisher, no drain loop and no depth cap. The ⚠️ at the end of this
> section — "Portfolio's repositories must **not** commit per call" — is withdrawn with it: Portfolio's
> repositories self-commit, exactly like Identity's.
>
> Left below as the record of what was planned and rejected. Reasoning in
> [00-overview.md](00-overview.md) §"Three modules, not four".

**This phase reintroduces the event type.** `Shared.Kernel` has no `IDomainEvent` — one was written in Phase 1 and deleted because nothing raised it, and an empty abstraction is worse than an absent one. `HoldingRemoved` is the first real event, so Phase 2 is where `IDomainEvent` (and whatever an entity needs to raise one) gets added, with a consumer waiting in Phase 4. Add the minimum the single consumer needs; do not restore the deleted `AggregateRoot<TId>` base along with it.

A `SaveChangesInterceptor`, not a `SaveChangesAsync` override — the interceptor keeps `DbContext` free of application-layer dependencies and is registered once per module context rather than duplicated.

```csharp
internal sealed class DispatchDomainEventsInterceptor(IDomainEventPublisher publisher)
    : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
    { … }

    // Throw from the sync overload so nobody accidentally bypasses dispatch.
    public override InterceptionResult<int> SavingChanges(…) =>
        throw new NotSupportedException("Use SaveChangesAsync.");
}
```

Dispatch **before** save so handler writes join the same transaction. Drain in a loop (handlers can raise new events), **clear before publishing** (otherwise the next loop re-collects the same events and never terminates), and cap the depth at 10 with a throw — a handler cycle otherwise hangs a request thread with no diagnostic.

Handlers must not call `SaveChangesAsync` themselves. They mutate tracked aggregates; the outer save persists everything. Enforce it in the architecture tests.

⚠️ This is the one place Portfolio departs from Identity, where there is no unit of work and each repository write commits (`phase-1-implementation.md` §5.3). Dispatch-before-save needs handler writes inside the same transaction, so Portfolio's repositories must **not** commit per call. Decide this explicitly when writing `IHoldingRepository` — a repository that commits and an interceptor that assumes it has not are silently incompatible.

Registered per context so scoped dependencies resolve correctly:

```csharp
services.AddDbContext<PortfolioDbContext>((sp, o) => o
    .UseNpgsql(cs, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "portfolio"))
    .AddInterceptors(sp.GetRequiredService<DispatchDomainEventsInterceptor>()));
```

### 2.3 Commands and queries

| Type | Result cases |
|---|---|
| `AddHoldingCommand(Guid UserId, string Ticker, decimal Quantity, decimal Price)` | `OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>` |
| `UpdateHoldingCommand(Guid UserId, HoldingId HoldingId, decimal Quantity, decimal Price)` | `OneOf<HoldingSummary, NotFound, InvalidInput>` |
| `RemoveHoldingCommand(Guid UserId, HoldingId HoldingId)` | `OneOf<Success, NotFound>` |
| `GetHoldingsQuery(Guid UserId)` | `IReadOnlyList<HoldingSummary>` |

Names carry the role — `AddHoldingCommand`, `AddHoldingCommandHandler` — and each lives in `Application/Holdings/Commands/AddHolding/`. The handler returns the union directly; there is no result-union class. `Success` and `NotFound` come from `OneOf.Types`. The request records the endpoints bind live in `Portfolio.Api/Requests/`, and a query with a single success shape needs no union at all.

✏️ **Corrected twice.** The feature-area folder was `Portfolio/`, which yields the namespace `…Portfolio.Application.Portfolio.Commands.AddHolding` — Identity deliberately picked `Authentication/`, a feature name that is not the module name, and Portfolio picks `Holdings/` for the same reason. And **`HoldingSummary` is the one name for the shared success payload**: §2.4 below called the same type `HoldingDto`, which does not exist anywhere. It sits at the `Application` root, in the position `TokenPair.cs` occupies in Identity. See `phase-2-implementation.md` §2.4.

`AddHolding` returns **two distinct success cases**. The UI needs to say "added" versus "merged into your existing position, new average $125" — collapsing them loses the one interaction where the domain rule is visible to the user.

`UnknownTicker` matters: the audit flagged that `Initial.md` never says what happens when someone adds `ASDFG`. Without this case it sits pending forever with no error anywhere, which is the worst outcome against the brief's "коректна обробка помилок і edge-кейсів". This phase validates shape only (`^[A-Z]{1,5}$`); Phase 3 upgrades it to a real symbol lookup once a provider exists.

Every handler takes `UserId` from the authenticated principal, never from the request body.

### 2.4 Endpoints

```
GET    /api/holdings           200 + HoldingSummary[]                        [Authorize]
POST   /api/holdings           201 + HoldingSummary | 200 + HoldingSummary (merged) | 400
PATCH  /api/holdings/{id}      200 + HoldingSummary | 404 | 400
DELETE /api/holdings/{id}      204 | 404
```

`POST` returning 201-or-200 is the honest encoding of create-or-merge. The `Location` header is set on **the 201 only**.

✏️ **Corrected, two ways.** `HoldingDto` was this section's second name for §2.3's `HoldingSummary`; there is one type and it is `HoldingSummary`. And `Location` was specified on both responses: a `Location` on a 200 is not *wrong*, but nothing reads it and expressing it means abandoning `TypedResults.Ok` for a hand-built result. The 201 carries it, and the README says so.

### 2.5 Persistence

`PortfolioDbContext`, schema `portfolio`, table `holdings`.

```csharp
b.ToTable("holdings");
b.HasKey(h => h.Id);
b.HasIndex(h => new { h.UserId, h.Ticker }).IsUnique();
b.Property(h => h.Quantity).HasPrecision(18, 6);
b.ComplexProperty(h => h.AveragePrice, m => {
    m.Property(x => x.Amount).HasColumnName("avg_price_amount").HasPrecision(18, 6);
    m.Property(x => x.Currency).HasColumnName("avg_price_currency").HasMaxLength(3).IsFixedLength();
});
```

`ComplexProperty`, not `OwnsOne`. Owned entities carry identity and reference semantics, so assigning the same `Money` instance to two properties throws on save; complex types copy by value. Microsoft explicitly recommends migrating.

Precision `(18,6)` on quantity and price, not `(18,2)` — fractional shares exist and an average of `$125.333333` must not silently round to `$125.33` before it reaches the P&L calculation in Phase 3.

---

### 2.6 Ticker search — added after the plan shipped

A new user logs in to an empty dashboard and must add a position. Without search that is a free-text box:
they type a symbol from memory, mistype it, and get a validation error with no way to discover the right
one. It is the **first** interaction a reviewer has with the application, and nothing in the six phases
covered it.

```
GET /api/tickers/search?q=appl   →   [ { symbol: "AAPL", description: "Apple Inc" }, … ]
```

Backed by Finnhub's symbol-search endpoint, in `MarketData` — it is the only module that talks to the
provider. Portfolio consumes it through `MarketData.Contracts` exactly as the dashboard consumes quotes.

Three things it settles at once: **discovery** (you can find a symbol you half-remember), **validation**
(a symbol picked from the list exists, so `UnknownTicker` stops being the common case), and **the company
name**, which the mockup's table wants and which nothing else was going to supply.

The field is a combobox — type, debounce, list, pick. Free text stays allowed and still validates against
`^[A-Z]{1,5}$`, so the endpoint being down degrades to what Phase 2 already had rather than blocking the
form.

⚠️ Search results are **not** cached in Redis. It is a user-typed query with no reuse across users, and
caching it would be the same instinct that produced the read-through mess in Phase 3.

---

## 3. Frontend

### Route

`src/routes/_authenticated/portfolio.tsx` — mockup's Portfolio screen: positions table (Asset, Qty, Buy, ×), an **Add position** form (ticker, quantity, buy price), and an **Invested** total. No price or P&L columns yet — those arrive in Phase 3.

### Data

```ts
export const holdingKeys = {
  all: ['holdings'] as const,
  list: () => [...holdingKeys.all, 'list'] as const,
}
```

Route `loader` calls `queryClient.ensureQueryData(holdingsQuery)` — holdings are stable route-defining data, so a loader is right here. Phase 3's quotes deliberately will **not** use a loader.

Mutations use optimistic updates for add, edit and delete.

⛔ ~~**TanStack Query v5.89.0 changed every mutation callback signature** — `onMutateResult` was inserted before a new `context` parameter. Every optimistic-update tutorial written before September 2025 rolls back incorrectly, restoring the wrong snapshot.~~

✏️ **False, and it was the only factual error in this file about a third-party library.** v5.89.0 *renamed* the `TContext` generic to `TOnMutateResult` and *appended* a new `context` (`{ client, meta, mutationKey }`) as the **last** parameter of each callback; `mutationFn` gained a second argument. Nothing moved: the `onMutate` snapshot is still argument 3 in `onError`/`onSuccess` and 4 in `onSettled`, so pre-5.89 rollback code compiles and restores the right value. Verified three ways against the pinned 5.101.4. The corrected wording lives in root `CLAUDE.md`'s Traps list; see also `phase-2-implementation.md` §0 item 7.

### Forms

`react-hook-form` + `zod` via `@hookform/resolvers/zod`. The schema stores message **keys**, not strings, so Phase 5's i18n can translate them without touching validation:

```ts
const addHoldingSchema = z.object({
  ticker: z.string().regex(/^[A-Za-z]{1,5}$/, 'errors.ticker.format'),
  quantity: z.coerce.number().positive('errors.quantity.positive'),
  price: z.coerce.number().positive('errors.price.positive'),
})
```

### Merge feedback

When the API returns 200-merged rather than 201-created, show an inline notice: *"Merged into your AAPL position — 20 shares, average $125.00."* This is the phase's demo moment; a silent row update hides the only interesting business rule in it.

### Components

`Table` (with the mobile card fallback from Phase 1), `NumberField`, `ConfirmDialog` — a hand-built dialog with focus trap, Escape-to-close and `aria-modal`. It is the one screen here that needs real keyboard handling.

✏️ **Corrected: there is no `Table` "from Phase 1".** Phase 1 shipped `Alert, AppShell, AuthLayout, Button, Card, Logo, Spinner, TextField` and no table and no card fallback. Phase 2 builds `Table` *and* its 375px card fallback from scratch, which is why it is a task of its own rather than a reuse.

---

## 4. Infrastructure delta

⛔ ~~**None.**~~ **Cloud infrastructure, none** — redeploy the API image and the SPA; the Bicep is unchanged. **Local and test wiring, three files.**

⛔ ~~The only additions are the `portfolio` schema migration — which the existing ACA migration job picks up automatically because it runs a bundle over all four contexts — and one new integration-test container dependency, which is already in the fixture.~~ ⚠️ Both halves are wrong: the Migrator is not a bundle and registers Identity only (`phase-2-implementation.md` §0 item 2), and there are three contexts, not four, because the Alerts project shells do not exist in the source tree.

✏️ **Corrected: "delta: none" is what made this section dangerous.** Nothing in Azure changes, which is what the sentence was reaching for — but three files had to be edited before Portfolio could migrate or be tested at all, and each fails in a way that looks like something else:

| File | Why | Failure if missed |
|---|---|---|
| `src/Api/appsettings.Development.json` | no `ConnectionStrings:Portfolio` key | every `dotnet ef` command builds the host, so migration *tooling* breaks before runtime does |
| `src/Migrator/Program.cs` | scans `ServiceDescriptor`s and registered **Identity only** | migrator prints "up to date", exits 0, API serves 500s against an empty schema — a P0-gate failure this section claimed was impossible |
| `Api.IntegrationTests` fixture | `SettingsFor` set one connection string, `ApplyMigrationsAsync` migrated one context, `ModuleDbContextInterceptors` had no `AddToPortfolio` | the container was already there; the *wiring* was not |

So the honest claim is narrower and still worth making: **front-loading infrastructure meant a whole feature phase cost zero *cloud* infrastructure work.** It did not cost zero configuration.

---

## 5. Tests

### Unit — `Portfolio.UnitTests`

| Test | Asserts |
|---|---|
| `Merge_TenAtHundredPlusTenAtOneFifty_GivesTwentyAtOneTwentyFive` | The canonical case from `Initial.md:104` |
| `Merge_ThreeSuccessivePurchases_WeightsCorrectly` | 10@$100 + 5@$200 + 5@$50 → 20@$112.50 |
| `Merge_PreservesPrecision_NoPrematureRounding` | 1@$0.333333 + 2@$0.666667 keeps six decimals |
| `Merge_ZeroQuantity_ReturnsInvalidInput` | Not a silent no-op |
| `Merge_NegativeQuantity_ReturnsInvalidInput` | |
| `Merge_DifferentCurrency_ReturnsInvalidInput` | |
| `Correct_ReplacesRatherThanAverages` | 20@$125 corrected to 10@$100 → 10@$100, not an average |
| ⛔ `Remove_RaisesHoldingRemoved_Once` | Withdrawn — no events; removal is `HoldingRepository.RemoveAsync` |
| ⛔ `Create_And_Merge_RaiseNoEvents` | Withdrawn — the held-ticker list is read live and nothing raises anything |
| `Ticker_Normalises_ToUppercase` | `aapl` → `AAPL` |
| `Ticker_RejectsTooLong` | `TOOLONG` invalid |

### ⛔ Unit — `Portfolio.UnitTests` (dispatch) — WITHDRAWN

All three are withdrawn with §2.2. There is no interceptor to test.

| Test | Asserts |
|---|---|
| ⛔ `Interceptor_ClearsEventsBeforePublishing` | Publisher sees each event once |
| ⛔ `Interceptor_DrainsEventsRaisedByHandlers` | A handler raising a new event gets it dispatched |
| ⛔ `Interceptor_ThrowsAtMaxDepth` | A handler cycle fails loudly instead of hanging |

### Integration — `Api.IntegrationTests`

| Test | Asserts |
|---|---|
| `AddHolding_ReturnsCreated_WithLocationHeader` | 201 |
| `AddHolding_SameTickerTwice_Returns200Merged_OneRowInDatabase` | Merge path end to end |
| `AddHolding_UnknownTickerFormat_Returns400` | |
| `UpdateHolding_ChangesQuantityAndPrice` | 200 and persisted |
| `UpdateHolding_OtherUsersHolding_Returns404` | **Not 403** — a 403 confirms the resource exists |
| `RemoveHolding_Returns204_ThenGetIsEmpty` | |
| `RemoveHolding_Twice_SecondReturns404` | |
| `AddHolding_ConcurrentSameTicker_OneRowSurvives` | Two parallel POSTs; the unique index holds and one returns a conflict rather than creating a duplicate |

### Integration — SQL injection, in `Api.IntegrationTests`

P0 req 6's sharpest line is *«Конкатенація рядків у SQL неприпустима за жодних обставин»*. There is no hand-written SQL in this project, so the evidence has to be a test rather than a file a grader can read.

⚠️ **The add-holding endpoint alone cannot carry the payload.** `ticker` is validated against `^[A-Z]{1,5}$` and `quantity`/`price` are `decimal` — a payload is rejected at the model boundary and never reaches the data layer. That is good defence, but it proves *validation*, not *parameterisation*. So the group has three tests doing three different jobs:

| Test | Asserts |
|---|---|
| `AddHolding_TickerWithInjectionPayload_Returns400` | `'; DROP TABLE portfolio.holdings; --` is rejected by validation, and `portfolio.holdings` still exists afterwards. Defence in depth |
| `AddHolding_GeneratedSql_ParameterisesTicker` | **The direct proof.** A `DbCommandInterceptor` captures `ReaderExecuting`/`NonQueryExecuting` command text; it contains `@p0`-style placeholders and the literal ticker value appears **only** in `DbParameter.Value`, never in `CommandText` |
| `Register_EmailContainingQuoteAndComment_RoundTripsExactly` | `o'brien'--@example.com` is a legitimately valid address. It stores and reads back byte-identical, and `identity.users` still exists. This is the one field in the app that genuinely accepts SQL metacharacters, so it is where the payload actually reaches the database |

The interceptor from the second test is worth keeping registered in the test fixture for the whole assembly, with an assertion that **no** captured `CommandText` ever contains a value that was supplied as user input. That turns parameterisation from something asserted once into an invariant checked across every integration test in the suite — which is a stronger claim than any single raw query could make.

✏️ **Corrected: two of those three already exist.** Phase 1 built them under different names —
`ParameterisationTests.Queries_NeverInlineUserInput_IntoCommandText` *is* the recording-interceptor proof, and
`HostileInput_IsStoredVerbatim_AndExecutesNothing` *is* the proposed
`Register_EmailContainingQuoteAndComment_RoundTripsExactly`. The fixture-wide interceptor this paragraph
proposes building is already built. Phase 2 adds only the ticker-validation test and registers the existing
interceptor for the new context.

### ⛔ Two rows that had lost their table

✏️ **Corrected: these sat below a prose paragraph with no header, so they rendered as literal pipe text and
were easy to miss.** Given one here so they are readable; neither is built in Phase 2.

| Test | Status |
|---|---|
| ⛔ `HoldingRemoved_EventDispatched_WithinSameTransaction` | Withdrawn with §2.2 — there is no event and no dispatch |
| ⛔ `NewHolding_AppearsInPollSet_WithNoEvent` | **Withdrawn, not merely moved.** It was moved to Phase 3 as `PollSet_ReflectsHoldingsImmediately_AfterAdd` while the poller still read held tickers from Portfolio. It no longer does — polling exists to build alert history, so the poll set is the tickers with an *active alert*, which Alerts owns. Adding a holding puts nothing in the poll set, so there is no longer an assertion to make |

### Frontend

`adds a position and shows it in the table` · `shows merge notice when the API returns 200` · `optimistic delete restores the row when the API fails` (this is the one that catches the v5.89 signature change) · `rejects a 6-character ticker before submitting`

---

## 6. Gotchas

**`ComplexProperty`, not `OwnsOne`.** Owned entity types are entity types — they have identity, so `a.Price = b.Price; SaveChanges()` throws "the same entity is being tracked". Complex types copy by value.

✏️ **Corrected: the sentence "EF 11 is already deprecating the owned-JSON path" is deleted.** It was unsourced and the EF 10 and EF 11 release notes say no such thing. `ComplexProperty` is still the right call, for the reason that *is* true and is stated above — owned types carry identity.

**Complex types cannot be constructor parameters of their container** (efcore#31621, open). `private Holding(HoldingId id, Money averagePrice)` fails with *"Cannot bind 'averagePrice'… only mapped properties can be bound to constructor parameters."*

**This does not require a parameterless constructor**, and an earlier revision of this file said it did — while §2.1 simultaneously showed `Money averagePrice` as a constructor parameter, so the two halves of the file contradicted each other. EF's documented behaviour settles it: *"Not all properties need to have constructor parameters… EF Core will set it after calling the constructor in the normal way."* So **omit only the complex member**. The all-args-minus-complex constructor binds normally, and the factory assigns `AveragePrice` afterwards — `private set` is reachable from inside the type:

```csharp
var holding = new Holding(HoldingId.New(), userId, ticker, quantity, now, now);
holding.AveragePrice = purchasePrice;
return holding;
```

Phase 1's rule survives intact: no parameterless constructor, and the factory is still the only way in. What changes is that `EfConstructorBindingTests.User_BindsEveryMappedPropertyThroughTheConstructor` cannot be copied verbatim — for `Holding` it must assert every **scalar** property binds, and note in one line that `AveragePrice` is set post-construction.

**Precision is set at the column, and EF will not warn you.** Without `.HasPrecision(18, 6)` Npgsql maps `decimal` to `numeric` with no precision, which works, but any later `HasPrecision(18,2)` silently truncates existing averages on the next migration.

**`AsNoTracking()` on a write path silently saves nothing.** A command handler that accidentally inherits a no-tracking default mutates a detached entity and `SaveChangesAsync` writes zero rows, with no error. Keep the read path and the write path separate.

✏️ **Corrected: this was titled "`AsNoTracking()` kills domain-event dispatch."** The dispatch half is moot — there are no domain events and no `ChangeTracker.Entries<T>()` scan. The save half is true on its own and is why the gotcha survives at all.

**A unique index is the only real guarantee.** `SELECT then INSERT` in a handler is a race.

⛔ ~~Catch `PostgresException` with `SqlState == "23505"` and map it to the merge path or a conflict result.~~

✏️ **Corrected: no catch. The race loser gets a 500, exactly as registration does.** Three reasons, in
`phase-2-implementation.md` §2.6:

- **The instruction as written is an infinite loop.** "Map it to the merge path" means retry, but a failed
  `SaveChangesAsync` skips `AcceptAllChanges`, so the entity is **still `Added`** and the retry re-sends the
  identical `INSERT`. A correct retry must detach `ex.Entries` and re-run from the query — real subtlety
  bought for a millisecond-wide window.
- **`ON CONFLICT DO UPDATE` is unreachable.** EF Core 10 cannot emit it without raw SQL, which is banned
  repo-wide.
- **It would also put Npgsql inside `.Application`**, which `LayerReferenceTests` forbids.

`phase-1-implementation.md` §5.3 removed precisely this catch from Identity and wrote the cost down; that
decision stands. Defence goes where it is cheap instead: the frontend disables the Add button while the
mutation is in flight, which removes the double-click that is the only realistic source. §5's
`AddHolding_ConcurrentSameTicker_OneRowSurvives` is rewritten to assert what is actually built — **exactly
one row survives** — rather than "one returns a conflict".

**404, not 403, for another user's holding.** Returning 403 tells an attacker the ID exists.

---

## 7. Your call

### The merge rule — `Portfolio.Domain/Holding.cs`

```csharp
public OneOf<Success, InvalidInput> Merge(decimal quantity, Money purchasePrice)
{
    // TODO(you): weighted average is specified; the edges are yours.
    //
    //   Rounding — the average of 1@$0.333333 and 2@$0.666667 is $0.555556 recurring.
    //     decimal will not round for you. Whatever you pick here shows up in every
    //     P&L number in Phase 3, and in the totals row, where errors accumulate.
    //     Options: round to 6dp on store, keep full precision and round only on
    //     display, or round to the currency's minor unit.
    //
    //   Zero purchase price — is a $0 buy a gift/transfer worth recording, or a typo?
    //     Accepting it drags the average toward zero and looks like a bug on the
    //     dashboard. Rejecting it means a real transfer can't be entered.
    //
    //   Fractional quantities — allowed (18,6), but is 0.0000001 a valid position
    //     or noise that should be rejected?
}
```

~10 lines. Decide before writing `Merge_PreservesPrecision_NoPrematureRounding`, since the test encodes the choice.

✏️ **Decided. All three open questions above are now settled**, and the tests encode them — the `TODO(you)` block stays as the record of what was open, but the answers are these:

| Question | Chosen | Why |
|---|---|---|
| Rounding | **6 decimal places, banker's (`MidpointRounding.ToEven`), applied on store** | `numeric(18,6)` would round on `INSERT` anyway; rounding here keeps the in-memory value and the persisted value identical, so a re-read never changes the number. `1 @ $0.333333 + 2 @ $0.666667 → $0.555556` |
| Zero purchase price | **Rejected — price must be strictly > 0** | A $0 buy drags the average toward zero and reads as a bug on the dashboard. A genuine transfer is not a purchase and does not belong in `Merge` |
| Dust quantity | **Rejected below `0.000001`** | That is one unit of the column's precision. `0.0000001` rounds to `0` in `numeric(18,6)`, and the next `Merge` would then divide by zero |

```csharp
AveragePrice = new Money(Math.Round(weighted, 6, MidpointRounding.ToEven), AveragePrice.Currency);
private const decimal MinimumQuantity = 0.000001m;
```

Restated in `phase-2-implementation.md` §3 "The merge arithmetic, settled", which is the copy the tests were written against.

---

## 8. Done when

- [ ] `docker compose up` → log in → `/portfolio`
- [ ] Add AAPL 10 @ $100 → row appears, Invested = $1,000
- [ ] Add AAPL 10 @ $150 → **still one row**, 20 shares, average $125, Invested = $2,500, merge notice shown
- [ ] Edit that row to 15 @ $120 → 15 shares @ $120, Invested = $1,800
- [ ] Delete it → table empty, Invested = $0
- [ ] Reload → still empty (it actually persisted)
- [ ] Add a position as user A, log in as user B → B sees nothing
- [ ] `dotnet test` green, including `AddHolding_ConcurrentSameTicker_OneRowSurvives`
- [ ] `npm test` green, including the optimistic-rollback test
- [ ] Deployed to Azure and the same walkthrough passes on the GitHub Pages URL
- [ ] README: the weighted-average rule with the worked example, and why Update replaces rather than averages
- [ ] Table readable at 375px as cards
