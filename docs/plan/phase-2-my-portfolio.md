# Phase 2 — My portfolio · 0.75 days

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
        HoldingId id, UserId userId, Ticker ticker, decimal quantity,
        DateTimeOffset createdAt, DateTimeOffset updatedAt);

    public HoldingId Id { get; private set; }
    public UserId UserId { get; private set; }
    public Ticker Ticker { get; private set; }
    public decimal Quantity { get; private set; }
    public Money AveragePrice { get; private set; }      // ComplexProperty → two columns
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static OneOf<Holding, InvalidInput> Create(
        UserId userId, Ticker ticker, decimal quantity, Money purchasePrice, TimeProvider clock);

    /// Merges a new purchase into this position: quantities sum, price becomes the
    /// weighted average. 10 @ $100 then 10 @ $150 → 20 @ $125.
    public OneOf<Success, InvalidInput> Merge(decimal quantity, Money purchasePrice);

    /// Direct correction of a mistyped entry. Not a purchase — replaces, never averages.
    public OneOf<Success, InvalidInput> Correct(decimal quantity, Money purchasePrice);
}
```

No base class — see `phase-1-implementation.md` §5.2. `Holding` declares its own `Id`, has exactly one private all-args constructor that only assigns, and no settable surface, so `Create` is the only way in.

`Merge` and `Correct` are deliberately separate operations. Overloading one method with a flag is how a "fix my typo" silently becomes a second purchase.

⛔ ~~Deleting raises `HoldingRemoved(UserId, Ticker)`, consumed by Alerts in Phase 4 to clear any pending cooldown for that user and ticker. `Create` and `Merge` raise nothing.~~ **Withdrawn.** Alerts is a feature area inside Portfolio, not a module, so Phase 4 clears the cooldown with a call at the end of `RemoveHoldingCommandHandler`. `Holding` raises nothing and has no event surface. See `phase-2-implementation.md` §0.0.

That asymmetry is deliberate and worth understanding, because an earlier draft had events on both. The poll set is read live from Portfolio at the start of every cycle (Phase 3 §2.5), so *adding* a holding needs no notification — the next cycle simply sees it. Only removal has an effect nothing else would notice: a cooldown key sitting in Redis for a position you no longer own. One event, one consumer, one real job.

The dispatch machinery below is still worth building for that single consumer — it is about forty lines, it is the seam every later cross-module event goes through, and Phase 4's `HoldingRemoved_ClearsCooldown` test exercises it end to end.

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
| `AddHoldingCommand(UserId, string Ticker, decimal Quantity, decimal Price)` | `OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>` |
| `UpdateHoldingCommand(UserId, HoldingId, decimal Quantity, decimal Price)` | `OneOf<HoldingSummary, NotFound, InvalidInput>` |
| `RemoveHoldingCommand(UserId, HoldingId)` | `OneOf<Success, NotFound>` |
| `GetHoldingsQuery(UserId)` | `IReadOnlyList<HoldingSummary>` |

Names carry the role — `AddHoldingCommand`, `AddHoldingCommandHandler` — and each lives in `Application/Portfolio/Commands/AddHolding/`. The handler returns the union directly; there is no result-union class. `Success` and `NotFound` come from `OneOf.Types`. The request records the endpoints bind live in `Portfolio.Api/Requests/`, and a query with a single success shape needs no union at all.

`AddHolding` returns **two distinct success cases**. The UI needs to say "added" versus "merged into your existing position, new average $125" — collapsing them loses the one interaction where the domain rule is visible to the user.

`UnknownTicker` matters: the audit flagged that `Initial.md` never says what happens when someone adds `ASDFG`. Without this case it sits pending forever with no error anywhere, which is the worst outcome against the brief's "коректна обробка помилок і edge-кейсів". This phase validates shape only (`^[A-Z]{1,5}$`); Phase 3 upgrades it to a real symbol lookup once a provider exists.

Every handler takes `UserId` from the authenticated principal, never from the request body.

### 2.4 Endpoints

```
GET    /api/holdings           200 + HoldingDto[]                    [Authorize]
POST   /api/holdings           201 + HoldingDto | 200 + HoldingDto (merged) | 400
PATCH  /api/holdings/{id}      200 + HoldingDto | 404 | 400
DELETE /api/holdings/{id}      204 | 404
```

`POST` returning 201-or-200 is the honest encoding of create-or-merge. The `Location` header is set on both.

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

⚠️ **TanStack Query v5.89.0 changed every mutation callback signature** — `onMutateResult` was inserted before a new `context` parameter. Every optimistic-update tutorial written before September 2025 rolls back incorrectly, restoring the wrong snapshot. Write against the current signature and check `onError` actually receives what you think.

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

---

## 4. Infrastructure delta

**None.** Redeploy the API image and the SPA; the Bicep is unchanged.

The only additions are the `portfolio` schema migration — which the existing ACA migration job picks up automatically because it runs a bundle over all four contexts — and one new integration-test container dependency, which is already in the fixture. ⚠️ Both halves are wrong: the Migrator is not a bundle and registers Identity only (`phase-2-implementation.md` §0 item 2), and there are three contexts, not four, since Alerts merged into Portfolio.

This is the point of front-loading infrastructure: a whole feature phase costs zero infrastructure work.

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
| ⛔ `Create_And_Merge_RaiseNoEvents` | Withdrawn — the poll set is read live and nothing raises anything |
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
| ⛔ `HoldingRemoved_EventDispatched_WithinSameTransaction` | Withdrawn with §2.2 — there is no event and no dispatch |
| `NewHolding_AppearsInPollSet_WithNoEvent` | Proves the live read works and the event isn't needed. Moved to Phase 3 as `PollSet_ReflectsHoldingsImmediately_AfterAdd` (`phase-2-implementation.md` §2.9) |

### Frontend

`adds a position and shows it in the table` · `shows merge notice when the API returns 200` · `optimistic delete restores the row when the API fails` (this is the one that catches the v5.89 signature change) · `rejects a 6-character ticker before submitting`

---

## 6. Gotchas

**`ComplexProperty`, not `OwnsOne`.** Owned entity types are entity types — they have identity, so `a.Price = b.Price; SaveChanges()` throws "the same entity is being tracked". Complex types copy by value. EF 11 is already deprecating the owned-JSON path.

**Complex types cannot be constructor parameters of their container** (efcore#31621, open). `private Holding(HoldingId id, Money averagePrice)` fails with *"Cannot bind 'averagePrice'… only mapped properties can be bound to constructor parameters."*

**This does not require a parameterless constructor**, and an earlier revision of this file said it did — while §2.1 simultaneously showed `Money averagePrice` as a constructor parameter, so the two halves of the file contradicted each other. EF's documented behaviour settles it: *"Not all properties need to have constructor parameters… EF Core will set it after calling the constructor in the normal way."* So **omit only the complex member**. The all-args-minus-complex constructor binds normally, and the factory assigns `AveragePrice` afterwards — `private set` is reachable from inside the type:

```csharp
var holding = new Holding(HoldingId.New(), userId, ticker, quantity, now, now);
holding.AveragePrice = purchasePrice;
return holding;
```

Phase 1's rule survives intact: no parameterless constructor, and the factory is still the only way in. What changes is that `EfConstructorBindingTests.User_BindsEveryMappedPropertyThroughTheConstructor` cannot be copied verbatim — for `Holding` it must assert every **scalar** property binds, and note in one line that `AveragePrice` is set post-construction.

**Precision is set at the column, and EF will not warn you.** Without `.HasPrecision(18, 6)` Npgsql maps `decimal` to `numeric` with no precision, which works, but any later `HasPrecision(18,2)` silently truncates existing averages on the next migration.

**`AsNoTracking()` kills domain-event dispatch.** `ChangeTracker.Entries<T>()` only sees tracked entities, so a command handler that accidentally inherits a no-tracking default saves nothing *and* dispatches nothing, with no error. Keep the read context and the write context separate.

**A unique index is the only real guarantee.** `SELECT then INSERT` in a handler is a race. Catch `PostgresException` with `SqlState == "23505"` and map it to the merge path or a conflict result.

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
