# Phase 2 — My portfolio

## Goal

Add AAPL 10 @ $100, then AAPL 10 @ $150, and end up with **one row: 20 shares at an average of $125**. Edit
that row. Delete it. All of it persisted, all of it working on the public URL.

Covers P0 requirement 4, portfolio CRUD. The original architecture essay designed create, read and delete
only. The brief's heading is CRUD, and a rubric row labelled CRUD looks for edit, so update is in scope here.
Correcting a mistyped purchase price is not a partial disposal and drags in no transaction history, so it
costs about an hour.

---

## A position, and what happens when you buy more

A position is one user's holding of one ticker: how many shares, and what they averaged. There is exactly one
row per user per ticker, and the database enforces that with a unique index — a check in code alone cannot
survive two requests arriving at once.

Buying more of something you already hold **merges**. The quantities sum and the price becomes the weighted
average: 10 at $100 plus 10 at $150 is 20 at $125. Editing a position **replaces** it: correcting a typo is
not a second purchase, and averaging a correction into the number it was meant to fix makes the mistake
permanent.

These are two separate operations on purpose. Overloading one operation with a flag is how "fix my typo"
silently becomes "buy some more".

**The user is told which of the two happened.** `POST /api/holdings` answers **201** when the position is new
and **200** when the purchase merged into one you already had, and the screen shows an inline notice —
*"Merged into your AAPL position — 20 shares, average $125.00."* Collapsing the two responses would hide the
one business rule in this phase that a user can actually see. The `Location` header is set on the 201 only;
on the 200 nothing would read it.

The four routes:

```
GET    /api/holdings           200
POST   /api/holdings           201 created · 200 merged · 400
PATCH  /api/holdings/{id}      200 · 404 · 400
DELETE /api/holdings/{id}      204 · 404
```

All of them require a bearer token.

---

## How much precision, and why

Money and share counts keep **six decimal places**, not two. Fractional shares exist, and an average of
$125.333333 must not quietly become $125.33 before it reaches the profit-and-loss arithmetic in the next
phase, where per-row errors accumulate into a totals row.

Rounding is to six places using banker's rounding, applied **when a value is stored**. The database column
would round on write anyway; rounding in the model as well keeps the in-memory value and the persisted value
identical, so re-reading a position never changes the number. Incoming amounts are rounded *before* they are
validated, so the rules judge the number the column will actually hold rather than the one the caller typed.

Three edges, all decided:

| Question | Answer | Why |
|---|---|---|
| A purchase price of zero | Rejected | It drags the average toward zero and reads as a bug on the dashboard. A genuine gift or transfer is not a purchase and does not belong in a merge |
| A quantity smaller than one millionth | Rejected | It rounds to zero on store, and the next merge would then divide by zero |
| A quantity or price above what the column holds | Rejected as a validation error | Including a merge whose *total* crosses the ceiling when each half cleared it alone. Otherwise the database raises a numeric overflow and the user gets a 500 where they should get a 400 naming the field |

---

## "Do I already own this?" is answered by looking, not by failing

The handler queries for an existing position and then merges or creates. It does not insert optimistically
and read a duplicate-key error back out of the exception.

Three reasons the exception route was rejected:

- **Mapping a duplicate-key failure to the merge path means retrying, and a naive retry never terminates.**
  After a failed save the new row is still pending insert, so the retry re-sends the identical statement and
  fails identically. A correct retry has to detach the failed entries and re-run from the query — real
  subtlety bought for a millisecond-wide window.
- **The single-statement "insert or update" is unreachable** without hand-written SQL, and raw SQL is banned
  across this repository.
- **It would put the database driver's exception type inside the application layer**, which the layering
  rules forbid.

Nor would a stricter isolation level help: unique-constraint enforcement is physical and sits beneath
serialisable snapshot isolation, so violations still surface there.

**The accepted cost:** two genuinely simultaneous purchases of the same ticker can both pass the look-up. One
wins the unique index and the other gets a **500 rather than a 409**. The index stays — it is what keeps the
data correct — and the window is a few milliseconds for one user on one ticker. Registration already accepts
exactly this trade for duplicate email addresses. Defence goes where it is cheap instead: the Add button is
disabled while the request is in flight, which removes the double-click that is the only realistic source.
If that 500 is ever actually seen in a log, the fix is a translation of the duplicate-key error in the
persistence layer, not the retry.

The test that guards this asserts **exactly one row survives** four concurrent purchases. It deliberately does
not assert the loser's status code, because that would be pinning an accident rather than a decision.

---

## A ticker is a value, not a string

A ticker owns its canonical form: trimmed, upper-cased, and checked against one-to-five letters at the moment
it is created. There is therefore exactly one definition of "the same symbol", used both when a position is
stored and when one is looked up — a look-up that normalises differently from the write simply misses. That
is what makes adding `aapl` merge into the `AAPL` you already hold.

The contrast worth remembering: an email address is a bare string with a canonicaliser hanging off the user
entity, because it has nowhere better to live. A ticker is a value in its own right and canonicalises inside
itself.

Each module that needs a ticker declares its own. Types crossing a module boundary carry plain strings, and
a module referencing a user it does not own stores a plain identifier rather than the owning module's
identifier type. The one place the ticker value type is deliberately absent is a command that identifies a
position by route parameter, where a plain identifier is what the caller already has in hand.

Shape validation happens twice, and that is the design rather than an oversight. At the HTTP boundary it
returns a 400 naming the field, which is the friendly message. On the entity it is an invariant, which is the
guarantee a future non-HTTP caller cannot bypass. Mixed-case input is accepted at the boundary and
canonicalised inside — rejecting `aapl` would be rejecting a correct request for looking untidy.

**An unrecognised symbol is a separate outcome from ordinary invalid input.** This phase only checks the
shape, so the two produce the same 400 today. They were kept apart because Phase 3 adds a real existence check
against the market-data provider on top of the shape check, at which point "there is no such stock" becomes a
different sentence and possibly a different status. Keeping them separate meant that upgrade changed one
handler rather than every place that maps an outcome to a response.

---

## What this screen deliberately does not show

The positions table has asset, quantity, buy price and actions, plus an **Invested** total. There is no live
price column and no profit-and-loss column.

That is not a gap waiting to be filled here. Those numbers cost one provider call per position, and putting
them on the CRUD screen would make every render of a page whose job is editing data pay for a fan-out it does
not need. The dashboard owns prices and P&L; this screen owns the data behind them.

---

## Money on the wire, and who computes it

Amounts are decimal server-side and leave as an object with the **amount quoted as a string**, so nothing
downstream can parse a price as a floating-point number. Inbound, a price arrives as an ordinary JSON number:
the user typing a price is not the browser computing money.

Each row's invested figure is computed on the server. The page total is summed in the browser from those
server-supplied figures purely for display, and is never recomputed as quantity × price — the server rounds
the average on store, and a float multiplication in the browser would disagree with it in exactly the place
errors are most visible.

---

## Ownership and errors

The user identifier always comes from the bearer token and never from a request body. Every read is filtered
by user at the repository, so no handler can forget.

The consequence is that another user's position returns **404, not 403**. A 403 confirms to a stranger that
the identifier exists.

---

## Storage

The module owns its own schema and, critically, **its own migration history table**. Setting a default schema
does not move the history table; without an explicit one per module, every module shares a single history
table, each sees the others' migrations as applied, and it looks exactly like data corruption.

The average price is stored as two columns — amount and currency — mapped as a value that copies by value
rather than as an owned entity. An owned type is an entity type and carries identity, so assigning the same
money instance to two properties fails on save. Its two members are mapped one at a time, for two reasons:
they have no setters and so are not mapped by convention at all, and column names have to be spelled out
because the PostgreSQL provider does not snake-case them for you. The cost of that explicitness is that a
member added later would be silently unmapped, so an assertion compares the mapped members against the
declared ones.

Two persistence rules inherited from Phase 1 and re-confirmed here:

- **Entity constructors stay free of guards.** The ORM binds a constructor by parameter name to materialise
  every row of every query, so anything inside one runs per row. Validation lives in the factory, which the
  ORM never calls. This applies to value objects too, not only entities.
- **There is no unit of work.** Each repository write persists before it returns; the context is already a
  unit of work and a second one over it only adds a name.

**A visibility flag ships in this migration, unused.** Every position carries a "show this on the dashboard"
flag, defaulted on, with no way to turn it off until Phase 5. One column now is cheaper than altering a live
table mid-demo later, and shipping it on the wire from the first response means Phase 5 adds behaviour rather
than a contract change. It is a *display* filter: the dashboard read honours it, but a hidden position is
still held, so reads that answer "does this user hold this?" must never narrow to visible ones.

**A new module has to be told to migrate.** The migration runner discovers contexts from what is registered
with it, not from a bundle over everything that exists. Registering the new module in the shared list is what
makes the schema appear; miss it and the stack starts, the runner reports "up to date", and every holdings
request fails against an empty schema — a P0-gate failure that looks like an application bug. The list lives
in one place that both the runner and the integration fixture use, so dropping a module from it fails the test
suite for the same reason it fails a clean `docker compose up`.

---

## Domain events: planned, and deliberately not built

An earlier design had removing a position raise an event, consumed later by alerts to clear a pending
cooldown, plus the machinery to collect and dispatch such events around a save.

None of it exists. The single event had a single job, and that job turned out not to need doing: a cooldown
key has a time-to-live and expires by itself. With no raiser, reintroducing the abstraction would have
repeated Phase 1's mistake of writing an interface nothing implements. Removing a position is a delete and
nothing more.

The one conclusion from that design that survived is worth keeping: **state the commit point on a repository
interface before the first handler exists.** A repository that commits per call and a dispatcher that assumes
it has not are silently incompatible, and the question outlives whichever answer a phase picks.

---

## Ticker search

Type a few letters, see matching companies, pick one. Specified for this phase and delivered after Phase 3,
because the symbols come from the price provider and the provider did not exist until then.

It is not the same thing as the symbol-existence check. That check answers *does this symbol exist?* after
the user submits, and is invisible until something is rejected. Search answers *what is Apple's symbol?*
while the user is typing. It is also the only thing in the system that produces a company name.

**The field is a text box first.** Someone who knows the symbol types it and submits, and never opens the
list. A provider outage returns no matches rather than an error, so the field simply behaves as it did
before this feature existed — which is the only behaviour that lets someone record a purchase they really
made while search is down. Picking from the list is a convenience and never a requirement.

**Only symbols this form would accept are offered.** The provider's search is fuzzy and also returns foreign
listings and longer symbols. Suggesting one would fill the field with a value the form then rejects, which
is worse than suggesting nothing. Fuzziness itself is wanted and kept — typing part of a symbol still finds
the whole one.

**Company names are cached; prices are not, and the two rules do not conflict.** A price is meant to change
every second, so a stored one is almost certainly wrong. A name is meant never to change, so a stored one is
almost certainly right. Names therefore live in the cache with a weekly expiry: not in the database, where
a company that renames itself would be wrong forever, and not fetched per page load, which would make the
holdings page depend on the provider being up. That page has no price column precisely so that it never
gains such a dependency, and a cosmetic field must not give it one.

**A missing name is the ordinary case, not a failure.** The row shows its ticker alone. Every position added
before this feature existed has no name, the cache expires weekly, and the cache being down costs names and
nothing else.

**Search results themselves are not cached.** A search term is whatever someone typed — arbitrary, different
for everyone, rarely repeated. Only the ticker-to-name mapping is worth keeping, and that is a small set,
identical for every user, read on every page.

---

## The screen

One route, reached from the nav bar, behind the authenticated layout.

- **Holdings load through a route loader.** They are stable, route-defining data. Phase 3's quotes
  deliberately will not use one.
- **Edit and delete update the list optimistically and roll back on failure. Add does not.** The server
  assigns the identifier and, on a merge, recomputes the average, so an optimistic row would flash a number
  that is about to change — and that number is the one thing this phase exists to get right.
- **The table becomes cards below 640px.** A horizontally scrolling table at 375px is unreadable, and the
  brief asks for a usable mobile layout rather than a shrunken desktop one.
- **The delete confirmation is a hand-built modal** with a focus trap, Escape to close and correct modal
  semantics. The brief bans UI component kits, so there is no dialog primitive to reach for.
- **Form validation messages are stored as keys, not sentences**, so Phase 5's translations need no change to
  the validation itself.

---

## Infrastructure

**No cloud change**: no new Azure resource, no template edit, no new package on either side. Front-loading the
infrastructure in Phase 1 is what bought that.

It was not, however, zero *configuration*. Three things had to change before the module could migrate or be
tested at all, and each fails in a way that looks like something else: the local connection string (every
migration command builds the host, so a missing key breaks the tooling before it breaks the app), the
migration runner's module list, and the integration fixture's wiring for a second schema and second
connection.

Two further test-side consequences, both of which the phase wanted:

- The migration test that asserts which schemas hold history tables finally has something to say, because
  there are now two.
- The suite-wide proof that no user-supplied value is ever concatenated into SQL had to be extended to the new
  context — and that extension needed an assertion of its own. When a task's deliverable is "an existing
  suite-wide assertion now also covers X", something has to assert that it does, or the coverage claim is a
  test that cannot fail on the mistake it exists to catch.

---

## Done when

- `docker compose up` from a clean clone, log in, reach the portfolio screen
- Add AAPL 10 @ $100 → one row, Invested $1,000
- Add AAPL 10 @ $150 → **still one row**, 20 shares, average $125, Invested $2,500, merge notice shown
- Edit that row to 15 @ $120 → 15 shares at $120, Invested $1,800
- Delete it → table empty; reload → still empty
- Add as one user, log in as another → the second sees nothing, and editing the first user's position by
  identifier returns 404
- The migration log names both schemas
- Money is a **string** in a real response, not a number
- Four concurrent identical purchases leave exactly one row
- The table is readable at 375px as cards
- The deployed URL passes the same walkthrough
- The README carries the weighted-average worked example and why editing replaces rather than averages

## Reference

These describe the shape of the system rather than the order it gets built in. They live in `docs/reference/`.

- [Data model](../reference/er-diagram.md) — the holdings table, its unique key and the money columns.
- [Module boundaries](../reference/module-boundaries.md) — why holdings and alerts are not one thing.
- [Module interactions](../reference/module-interactions.md) — what Portfolio is allowed to ask of other modules.
