# Module rules

Loaded whenever a file under `src/Modules/` is read. `Identity` is the only module built; it is the
worked example, and [Identity/CLAUDE.md](Identity/CLAUDE.md) maps each rule to the file that shows it.

Root `CLAUDE.md` owns the *conventions* — naming, folder layout, the traps list. **This file does not
restate them.** It holds what is only visible in code shape, plus the rules Identity is too simple to
have taught.

**Read §3 before writing the first line of a new module.** Copying Identity blindly is wrong in five
specific places, and each one fails silently.

---

## 1. Entities

**One publicly reachable way in: the static factory.** The constructor is private, its parameter names
match their properties exactly, and it contains nothing but assignments. No parameterless constructor,
no object initialiser, no public setter.

**The constructor must stay guard-free.** EF binds it by parameter name, ignoring accessibility, so it
materialises every row — a guard inside it runs on every row of every `SELECT`. Guards live in the
factory, which EF never calls. Rename a parameter without renaming its property and the *whole model*
fails at host startup, not on first query.

**A complex type cannot be a constructor parameter** ([efcore#31621](https://github.com/dotnet/efcore/issues/31621),
open). `Holding` maps `Money AveragePrice` as a `ComplexProperty`, so `private Holding(…, Money averagePrice, …)`
fails model building with *"Cannot bind 'averagePrice'… only mapped properties can be bound to constructor
parameters."*

You do **not** need a parameterless constructor to solve this, and an earlier draft of the Phase 2 plan
said you did. EF's documented behaviour: *"Not all properties need to have constructor parameters… EF Core
will set it after calling the constructor in the normal way."* So omit **only** the complex member. The
factory assigns it after construction — legal, because `private set` is reachable from inside the type:

```csharp
var holding = new Holding(HoldingId.New(), userId, ticker, quantity, now, now);
holding.AveragePrice = purchasePrice;   // complex type: set after construction, not via the ctor
return holding;
```

Say in one line which properties EF sets post-construction, because "a half-built entity is not
representable" stops being literally true the moment a complex type is mapped.

**Three input sources, not two.** Identity only ever saw two, so its factories look like a binary rule:

| Source | Response | Why |
|---|---|---|
| The end user typed it | return `InvalidInput` | reaches the client as a 400 naming the field |
| A trusted in-process caller computed it | **throw** | no legitimate caller can get it wrong |
| An **external system** supplied it | a named result case (`UnknownTicker`, `MalformedQuote`) | expected runtime condition, and one bad ticker must not kill a poll cycle |

The third row is Phase 3's. A throw there is swallowed by the poller's in-loop `catch` and surfaces nowhere.

**`InvalidInput.Field` names the domain concept, not the wire field.** Identity's concept and wire name are
the same word (`email`), so the distinction never showed. From Phase 2 they diverge — `Money purchasePrice`
in the domain, `price` on the wire. The domain names what it rejected; `.Api` re-keys it onto the request
property before it becomes a `ProblemDetails` key. Get this wrong and the message lands in the page banner
instead of under the field.

**Ids: distinguish owned from carried.** An id the module *owns* is `readonly record struct <Entity>Id(Guid Value)`
with `New() => new(Guid.CreateVersion7())`, mapped `ValueGeneratedNever()`. An id it merely *carries* from
another module crosses as a raw `Guid` — see §3.

**Precision is not tightness.** For strings and byte arrays, keep the domain bound at least as tight as the
column so an over-long value is a 400 rather than a bodyless 500. For `numeric(p,s)`, Postgres **rounds
silently** — there is no error to catch, and a column tighter than the domain stores a different number than
the factory validated. The factory must produce a value already at the stored scale, and the column's
`(precision, scale)` must be asserted **equal** to what the domain guarantees.

**Mutators change tracked state and do not persist.** They return `OneOf<Success, InvalidInput>`; the write
belongs to the caller.

---

## 2. Handlers and `.Application`

**One clock reading per set of values that must agree.** Not "once per use case" — Identity's refresh path
reads it four times, correctly, because *is this still valid*, *when does the replacement expire* and *when
was this superseded* are different questions. The rule is that values which must be consistent with each
other derive from **one** reading. `SessionOpener` is the worked example: one `now` feeds the access expiry,
the JWT `exp` and the refresh expiry, so they cannot drift.

**Context questions are answered here, as a result case** — a `Find…` that returns null, then the failure
record. Not by reading a SQLSTATE out of an exception in `.Infrastructure`. Where a unique index is the only
real guarantee (Phase 2's `(user_id, ticker)`), the check still lives here and the index is the backstop;
the accepted cost is that a genuine race surfaces as 500 rather than 409.

**A failure record carries what the caller may act on.** Identity's are all empty because any detail there is
account enumeration. That is an authentication artefact, not a general rule — from Phase 2, `UnknownTicker`
should carry the ticker.

**`.Application` references no `Microsoft.*`.** Its csproj holds its own `.Domain`, other modules'
`.Contracts`, and `OneOf`. Logging is the host's decorators; configuration arrives as a constructed value.

**Code that runs outside an HTTP request has no principal and no framework to turn an exception into a
response.** Phase 3's poller and Phase 4's evaluator must handle their own failures; an unhandled exception
in a `BackgroundService` kills the host.

---

## 3. Where Identity's answer is NOT the general answer

Five places. Each one silently does the wrong thing if copied.

| # | Identity does | Why it does not generalise | Decide before writing the module |
|---|---|---|---|
| 1 | **Repositories self-commit** — every write calls `SaveChangesAsync` | Phase 2 dispatches domain events *before* save, so handler writes must land in one transaction. A repository that commits and an interceptor that assumes it hasn't are silently incompatible | State the commit point on the repository interface's doc comment **before the first handler is written**. Portfolio's repositories must not commit per call, and a test must pin it |
| 2 | **Eager config validation in `Add<M>Module`** | For MarketData a missing API key is a *supported* state — `FakeQuoteProvider` is what makes `docker compose up` work with no key, which is the P0 gate. Eager validation there breaks acceptance | Validate eagerly only what the module cannot run without. Do deferred-work **C8** (split `Add<M>Persistence`) at the second module |
| 3 | **Converters are for ids** | Identity has no non-id value object, so "id" and "converter-backed type" name the same set. Phase 2 adds `Ticker`, Phase 4 adds `AlertDirection` | Register `Properties<T>().HaveConversion<>()` **and** `DefaultTypeMapping<T>().HasConversion<>()` for every converter-backed type, not just ids |
| 4 | **Canonical form is a `public static` on the entity** (`User.NormaliseEmail`) | Only because email is a bare `string` with nowhere else to live. `Ticker` is a value object and canonicalises in its own factory | Canonicalise in the value object when there is one; a static on the entity only for bare primitives |
| 5 | **Exactly two host wire-ups finish a module** | True only because Identity has no background service, no adapter over another module's contracts, and no Redis | Count the wire-ups the module actually needs; "I called `Add` and `Map`" does not mean it is wired |

**Unresolved, and it blocks Phase 2:** `UserId` is `Identity.Domain`'s type, but `Holding` needs one.
`.Contracts` holds primitives only, so it cannot cross as `UserId`. Decide **before the first Portfolio
configuration is written** whether each module declares its own id types and the handler wraps the raw
`Guid` from the principal — the current architecture implies yes — and write it down.

---

## 4. `.Api`

**Requests live here, never in `.Application`.** The endpoint binds `<UseCase>Request` and constructs the
command with `new`. Only these records reach `/openapi/v1.json`.

**A route parameter stays its own delegate parameter** and never enters the request record. The user id comes
from the principal, not the body.

**Endpoint handlers return `Task<IResult>`** and map via `.Match<IResult>` with **every lambda parameter
named** — `emailTaken =>`, not `_ =>`.

**`.Produces` metadata is the only description of what a route emits** now that the typed union is gone, so
it can drift silently. `EndpointMetadataTests` is what catches that: drive the route over real HTTP, assert
the observed status, *then* assert it was declared. A new module extends that theory.

**One route may declare more than one 2xx** — Phase 2's `POST /api/holdings` returns 201 created or 200 merged.

**Degradation is a field on the success payload, not a failure status.** A stale quote is a 200 with a
freshness field; a dependency outage must not turn a working page into an error page.

**A route whose client cannot send headers is not `.RequireAuthorization()`.** Phase 4's SSE stream uses the
single-use ticket already designed into the overview — `EventSource` cannot set an `Authorization` header.

---

## 5. Tests

**A test that cannot fail is worse than none.** For every test added, name the mutation that makes it red. If
you cannot, delete it.

**A rule that passes by finding nothing needs a companion that fails if the search finds nothing.** Three
architecture rules shipped green while enforcing nothing before this was added.

**Every new mapped entity gets a constructor-binding test.** The assertions that carry weight: the entity type
is found, and a constructor binding exists. Note that "binds every mapped property" is only true while the
entity maps no complex type — see §1.

**Money on the wire is asserted on raw JSON** (`JsonDocument`, `GetProperty(...).GetString()`), never on a
deserialised type — deserialising re-parses and hides the very thing being asserted.

**Streaming tests are bounded and assert on what arrived**, never on a timeout.

**The shared fixture must not run background pollers on the real clock.** Register hosted services off, or
bind them to a `FakeTimeProvider`.
