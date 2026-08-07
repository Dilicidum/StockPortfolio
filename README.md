# StockPortfolio

Stock-portfolio tracker: live quotes, profit/loss, and threshold alerts pushed in real time.
.NET 10 modular monolith + React 19 SPA, Postgres and Redis, all of it up with one command.

> **Status: all six phases are built.** Authentication, routing and session persistence (Phase 1);
> portfolio CRUD with create-or-merge (Phase 2); the live dashboard — real prices, totals, profit and
> loss, weights and honest freshness stamps (Phase 3); threshold alerts pushed over SignalR with the
> price poller behind them (Phase 4); the settings surface — theme, English and Ukrainian, refresh
> interval, per-position visibility and a per-user market-data key encrypted at rest (Phase 5); and
> graceful failure — every dependency can be stopped and the app stays usable (Phase 6). Phase 6 is
> green in both test suites and has not yet been walked through by hand or deployed.
> See [docs/plan/00-overview.md](docs/plan/00-overview.md).

---

## Run it

```bash
git clone <repo> && cd StockPortfolio
docker compose up
```

That is the whole thing — frontend, API, Postgres and Redis. **No API key is needed**; with no
`Finnhub__ApiKey` configured the app falls back to a fake quote provider and logs a warning. That is
deliberate rather than lazy: Finnhub shut down its sandbox in September 2022, so a grader who had to
register for a key would be blocked before seeing anything.

| URL | What |
|---|---|
| <http://localhost:5173> | the app |
| <http://localhost:8080/health/live> | is the process up — touches no dependency |
| <http://localhost:8080/health/ready> | the four database logins and the cache, named one by one |
| <http://localhost:8080/health/startup> | are all migrations applied |
| <http://localhost:8080/openapi/v1.json> | OpenAPI document (Development only) |

`.env` is optional — every compose variable has a working default. Copy `.env.example` to `.env` to
change passwords.

### Developing

```bash
dotnet build                              # whole solution
dotnet test                               # unit + architecture + integration (integration needs Docker)
npm --prefix src/Web install
npm --prefix src/Web run dev
npm --prefix src/Web test
```

### Checking the whole thing by hand

Start from a clean clone with no API key configured. The fake price provider has to carry this on its
own, because there is no demo key to hand anybody.

1. Register, land on the dashboard, hard-refresh — still signed in.
2. Start typing `appl` in the ticker field. Apple appears with its name; arrow down and press Enter to
   fill the field. Then clear it and type `AAPL` by hand — that still works and always will.
3. Add 10 shares of AAPL at $100, then 10 more at $150. One row, 20 shares, average $125, and the row
   carries the company name that step 2 cached.
4. Prices appear on the first render, including for a stock added seconds ago.
5. Set a threshold to 1% and press Simulate — an alert arrives in about a second.
6. Reload. The alert is still listed, because it was saved, not replayed.
7. Switch to Ukrainian and to dark mode. Both survive a reload.
8. Hide a position. Its row goes; it is still watched and still alerts.
9. Set the refresh interval to 15 seconds. The dashboard visibly updates faster.
10. Block the price provider. Prices go amber, each labelled *last known* with its age, and the banner
    says the quote provider is not responding. Nothing crashes, and the ticker field still accepts a
    typed symbol — it just stops suggesting. The health card's **feed row goes amber**: the poller is
    still finishing its cycles on time, but it is storing nothing, and a punctual cycle that fetched no
    prices is a dead feed rather than a healthy one.
11. Narrow the window to 375px. The table becomes cards.
12. `docker compose down && docker compose up`. The data is still there.

Then stop the dependencies one at a time and watch the app stay usable. This is the part worth doing
slowly, because each one degrades differently on purpose.

```bash
docker compose stop redis      # dashboard still renders FRESH prices; the alerts panel says alerts are suppressed
docker compose start redis     # recovers on its own within one poll cycle, and the banner clears
docker compose stop postgres   # 503 with a readable message and a retry button — and check `docker compose ps`
```

13. **Redis stopped.** The dashboard is unchanged and the prices are live, not stale. The alerts panel
    grows a banner saying alerts are suppressed, and the health card shows the cache as degraded. The
    API stays in rotation — `curl localhost:8080/health/ready` still answers **200**.
14. **Redis started again.** The banner clears by itself within a poll cycle. Nothing to click.
15. **Postgres stopped.** Reads and writes answer **503** with a `Retry-After` header and the retry
    screen, in a couple of seconds rather than a minute. Watch `docker compose ps`, not the browser:
    the API must **not** be restart-looping, and a restart loop looks fine for the first few seconds.
    `/health/live` still answers 200 while `/health/ready` answers 503, which is the whole point of
    splitting them.
16. **Kill the API itself** (`docker compose stop api`). A thin reconnecting bar appears above the
    page, the alert badge stops claiming a connection, and the browser keeps retrying for as long as
    it takes — leave it five minutes and it still recovers when the API comes back. Reload the page
    while the API is down and it still recovers, which is a separate path from reconnecting.
17. **A deliberately bad key.** Put nonsense in `FINNHUB_API_KEY` in `.env`, `docker compose up -d api`,
    and the app *starts*. It does not silently swap to the fake provider — that would serve invented
    prices for real tickers on a screen claiming to show real ones. The health card says the provider
    rejected the key and names that as the reason, and the dashboard falls back to last-known prices.

Then repeat steps 1 to 6 against the deployed site, watching that the alert connection survives past
four minutes — the hosting platform closes idle connections at four minutes and will not go higher,
which is the whole reason the library sends a keepalive about every fifteen seconds.

**Two dash-rule cases cannot be provoked by hand in one sitting**, and are proven by unit test
instead of being left on this list looking checked: "blocked into a second trading hour, so the column
dashes" needs an hour of open US market with the provider blocked, and "blocked over a weekend, so
Friday's numbers stay" needs a weekend.

---

## Architecture

Four modules — `Identity`, `Portfolio`, `MarketData`, `Alerts` — each five projects, plus
`Shared.Kernel`, `Shared.Api`, the `Api` host and a `Migrator`. Thirty-one projects in all, seven of them
test projects.

**All four modules have a database.** One `DbContext` and one Postgres schema per module is the rule, and
MarketData was the stated exception for three phases: everything it persisted was a Redis key per ticker, so
an empty context would have bought a zero-table migration for no behaviour. Phase 5 ended that — the
`marketdata` schema now holds the per-user provider keys and the key ring that encrypts them.

```
.Api  ──▶ Application ──▶ Domain ──▶ Shared.Kernel
  │            ▲
  └─ HTTP only └── Infrastructure implements the abstractions
```

**Two reference rules are enforced by the compiler and asserted by tests:**
`.Infrastructure` never references ASP.NET Core, and `.Api` never references EF Core or its
own `.Infrastructure`. The two halves of a module meet only through `Application/Abstractions`, so a
route physically cannot reach a `DbContext` — the reference does not exist.

Only `.Contracts` crosses a module boundary. Since `.Domain` and `.Application` are `public` (they
have to be — `internal` is per-assembly and a module is five assemblies), the compiler cannot check
that one, so `Architecture.Tests` does. Those tests actually enforce the rule rather than describe it,
and there is a test that deliberately looks for references that *do* exist, so a search that always
found nothing could not report a false green.

### Decisions worth defending

**Alerts is its own module, not part of Portfolio.** Sharing a word does not make two models one:
`Ticker` means a stock symbol identically in Portfolio, MarketData and Alerts, and they are still three
contexts. Different words are enough to prove two contexts exist, but they are not required.

What decides it is the cost of pulling a module out — would this boundary survive becoming a network
call? `AlertSettings` and `FiredAlert` never share a transaction with `Holding`, no rule spans any two of
the three aggregates, they are written at different times, and alerts can be down while the dashboard
renders. Three aggregates with nothing spanning them is not one context.

**Core / supporting / generic subdomain labels are not used.** That vocabulary is real DDD and it
changed no code here; worse, it mixes up three separate ideas — a subdomain is the problem, a bounded
context is a model boundary, and a module in Evans' sense is a namespace *inside* a context. Boundaries
are argued from the cost of pulling a module out instead, which is something you can check.

**There is no domain-event machinery.** `HoldingRemoved` was the only domain event in a six-phase plan
and existed solely to clear a Redis cooldown across the Portfolio/Alerts line. A cooldown has a TTL and
clears itself, so the answer was to delete the event, not to move the boundary — and `IDomainEvent`, a
handler interface, a publisher and a `SaveChanges` interceptor were never written. The runtime
dependency graph is three edges — `Portfolio → MarketData`, `Alerts → MarketData`, `Alerts → Portfolio` —
nothing depends on Alerts, and nothing depends on Identity.

**SignalR over WebSockets, which is what the brief lists.** This was built twice. The first version
was hand-written server-sent events, chosen because alerts are strictly one-way and a full-duplex
protocol buys a direction nothing uses. That reasoning was sound and the conclusion was still wrong:
one-way is an argument about the *protocol*, and what it cost was our own code.

| | Hand-written SSE | SignalR over WebSockets |
|---|---|---|
| Matches the brief | no — a stated technology, missed | yes |
| Fan-out across replicas | our Redis pub/sub, ~75 lines | `AddStackExchangeRedis(...)`, one line |
| Auth without a header | our ticket: mint, store, redeem, 86 lines | `accessTokenFactory`, one line |
| Reconnect | our backoff, our state machine | `withAutomaticReconnect([...])` |
| Keeping the connection open | our 20-second `ping` frame | built in, about every 15s |
| Sticky sessions | not needed | not needed, given WebSockets-only + `skipNegotiation` |
| Readable with `curl` | yes | no |

About 450 lines of ours became about 60. The one thing genuinely lost is that the wire is no longer
plain text you can read in a terminal. What the choice still forces — where the credential travels,
and which claim decides who a message is for — is under [Alerts](#alerts).

**No raw SQL.** The brief permits raw SQL or a query builder and asks only for parameterisation.
EF Core makes parameterisation structural rather than a discipline. Here it is happening — the sign-in
lookup, copied out of the running API's own logs after signing in as a user whose address is an
injection attempt. Only the line wrapping is ours:

```sql
SELECT a."Id", a."AccessFailedCount", a."ConcurrencyStamp", a."Email", a."EmailConfirmed",
       a."LockoutEnabled", a."LockoutEnd", a."NormalizedEmail", a."NormalizedUserName",
       a."PasswordHash", a."PhoneNumber", a."PhoneNumberConfirmed", a."SecurityStamp",
       a."TwoFactorEnabled", a."UserName"
FROM identity."AspNetUsers" AS a
WHERE a."NormalizedUserName" = @normalizedUserName
LIMIT 1
```

| Placeholder | Value carried |
|---|---|
| `@normalizedUserName` | `SQLIDEMO'-OR-1=1--@EXAMPLE.TEST` |

The quote, the `OR 1=1` and the comment marker are all *inside the parameter*. The statement text does
not change shape, so there is nothing for them to change: the lookup matches that one literal address
and signs that one user in. The address is stored and returned verbatim, character for character.

**And the whole suite is watched, not just that one statement.** `RecordingDbCommandInterceptor` is an
EF `DbCommandInterceptor` wrapped around every module's `DbContext` in the integration fixture. It
records the text and the parameters of every command any test causes — reads, writes and scalars, sync
and async. `ParameterisationTests` then registers and signs in as `sqli<random>'-or-1=1--@example.test`
and asserts four things: that commands were actually recorded (so the interceptor cannot pass by being
unwired), that the random marker appears in **no** `CommandText`, that it *does* appear in a parameter
value (so the hostile string reached the database rather than being filtered out upstream, which would
prove nothing), and that every parameter on those commands is referenced by name from the statement.

**One Postgres role per module, and no cross-schema grants.** `portfolio_svc` selecting from anything in
the `identity` schema fails with SQLSTATE `42501`, and a second test reads `has_schema_privilege` directly
so the denial is shown to be the missing `USAGE` grant rather than a missing table. There is a test for exactly that, because a module
boundary you cannot demonstrate is a diagram, not a boundary. All four roles are in use — `identity_svc`,
`portfolio_svc`, `alerts_svc` and, since Phase 5, `marketdata_svc`, which sat created and inert until the
per-user provider keys needed it.

**Money is `decimal` server-side and serialised as strings.** `System.Text.Json` writes `decimal` as
a JSON number and `JSON.parse` turns it into a double, which destroys the arithmetic at the
boundary. Percentages and weights are computed server-side for the same reason.

**Zero UI component libraries.** No Radix, no Headless UI, no React Aria — the brief bans UI kits and
its list ends with "тощо" (etc.). Every control is hand-built with Tailwind.

### Token storage — the honest version

- **Access token in memory only.** A module-scoped variable, never `localStorage`.
- **Refresh token in `sessionStorage`**, in every deployment. **There is no cookie.** The server sets
  no cookie anywhere; every auth endpoint returns the token pair in the response body.

`sessionStorage` is weaker than an httpOnly cookie and it is the honest consequence of hosting the
SPA statically on a different origin from its API: the cookie would be third-party, and Safari
blocks those outright. It argues for a short refresh-token lifetime, and the lifetimes are ASP.NET Core
Identity's: the host sets the access token to 15 minutes, and the refresh token keeps the framework's
14-day default.

Being tab-scoped, `sessionStorage` also meant a second tab started with no credential and bounced to
`/login` while the first was still signed in. Rather than move the token somewhere shared — which on
a static cross-origin SPA means `localStorage`, i.e. a 14-day credential any injected script can read
— the session is **handed between tabs over `BroadcastChannel`**: a new tab asks, a live tab answers,
and nothing is ever written to disk. Every rotation is broadcast too, so the tab that did not refresh
does not end up holding a superseded token. Close every tab and the session still ends. See
`src/Web/src/auth/sessionChannel.ts`.

Rotation is on, with a short grace period so two open tabs refreshing at once do not log each other
out. Reusing a superseded token after the grace window closes is rejected.

---

## Portfolio

One row per `(user, ticker)`, enforced by a unique index rather than by a C# check — a check in a handler
cannot survive two requests arriving at the same time.

### Buying more of something you already hold averages the price

```
add AAPL  10 @ $100      →  10 shares, average $100,   invested $1,000
add AAPL  10 @ $150      →  20 shares, average $125,   invested $2,500
```

Not two rows, and not `$150`. The average is quantity-weighted:
`(10 × 100 + 10 × 150) / 20 = 125`. It is the one business rule in the phase that a user can actually see, so
the API distinguishes the two outcomes and the UI says which happened — *"Merged into your AAPL position — 20
shares, average $125.00."* A silent row update would hide it.

**Rounding: 6 decimal places, banker's (`MidpointRounding.ToEven`), applied on store.** Not on display.
The column is `numeric(18,6)`, so `INSERT` would round to six places regardless; doing it in the domain keeps
the in-memory value and the persisted value identical, so re-reading a position never changes the number.
`1 @ $0.333333` merged with `2 @ $0.666667` gives `$0.555556` — six decimals, because an average of
`$125.333333` silently becoming `$125.33` would carry into every P&L figure and every total, where the error
compounds.

That guarantee only holds if **every** write path rounds, and for the **quantity** as well as the price — the
response body is built from the in-memory entity, so a path that skips it returns a number the next `GET`
contradicts. `Holding` therefore rounds in one private helper, called by `Create`, `Merge` and `Correct`
alike, *before* the values are validated: the rules judge the number the column will actually hold. It also
matters that the mode is the domain's rather than the column's — Postgres rounds half away from zero, so
`$1.0000005` persists as `$1.000001` where banker's gives `$1.000000`.

Three edges are rejected rather than accepted quietly: a purchase price of **$0** (it drags the average toward
zero and reads as a bug on the dashboard), a quantity below **0.000001** (one unit of the column's
precision — `0.0000001` rounds to zero on store, and the next merge would then divide by zero), and any value
above **999999999999.999999**, which is simply more than twelve integer digits and would otherwise reach
Postgres as a `22003 numeric field overflow` and come back as a bare 500. The ceiling is checked in the request
validator so the client gets a 400 naming the field, and again in the entity — including against the *sum* of
a merge, which two individually legal quantities can cross.

### Editing replaces. It does not average.

Correcting a mistyped purchase is **not** a second purchase. A position of 20 @ $125 corrected to 10 @ $100
becomes exactly 10 @ $100 — the old numbers are wrong and are discarded, not blended in.

So `Merge` and `Correct` are two operations on the aggregate, not one with a flag. Overloading a single method
with `bool isCorrection` is precisely how a fix silently becomes a buy: the two paths look the same at the call
site, the flag gets defaulted or inverted once, and the resulting average is wrong in a way no test that
doesn't already know about the bug will catch.

### `POST /api/holdings` answers 201 **or** 200

| Response | Means |
|---|---|
| `201 Created` + `Location` | this position did not exist a moment ago |
| `200 OK` | the purchase merged into a position you already held |

That is the honest encoding of create-or-merge, and both are declared in the OpenAPI document. **`Location` is
set on the 201 only.** A `Location` on the 200 is not wrong — the resource does have a URL — but nothing reads
it, and expressing it means abandoning `TypedResults.Ok` for a hand-built result to say something no client
asks about.

Reading another user's holding returns **404, not 403**. A 403 confirms the id exists.

### Money crosses the wire as a string outbound and a number inbound

Deliberately asymmetric, for two different reasons.

**Out, a string.** `System.Text.Json` writes `decimal` as a JSON number and `JSON.parse` turns that into a
double, which destroys the arithmetic at the boundary — the value is exact in Postgres, exact in C#, and
lossy the moment a browser parses it. A converter in `Shared.Kernel` writes
`{"amount":"125.000000","currency":"USD"}` instead. Percentages and weights are computed server-side for the
same reason.

**In, a plain number.** `AddHoldingRequest.Price` is a `decimal` bound from an ordinary JSON number. The host
sets `JsonNumberHandling.Strict`, which forbids a *quoted* number binding to a numeric — so a string would in
fact be rejected. This does not weaken the rule: the rule is that the browser must never *compute* money, and
a user typing `150` into a price field is not a computation. Nothing on the client does arithmetic on it.

### The merge race, and the 500 we accept

Two genuinely simultaneous `POST`s for the same ticker can both read "no existing position" and both try to
insert. The unique index catches the second one, and it comes back as a **500, not a 409**.

That is a deliberate repeat of the decision already made for registration, not an oversight:

- **The obvious fix is an infinite loop.** Catching SQLSTATE `23505` and retrying into the merge path re-sends
  the identical `INSERT` — a failed `SaveChangesAsync` skips `AcceptAllChanges`, so the entity is still
  `Added`. A correct retry has to detach the failed entries and re-run from the query.
- **The atomic fix is unreachable.** `ON CONFLICT DO UPDATE` would express the whole merge in one statement,
  but EF Core 10 cannot emit it without raw SQL, and raw SQL is banned repo-wide.
- **The window is a millisecond wide**, and the only realistic source of it is a double-click — which the
  frontend removes by disabling the button while the mutation is in flight.

The index stays, because it is what keeps the data correct. What is tested is the thing that actually matters:
after two parallel posts, **exactly one row survives**. If that 500 ever shows up in practice, the catch comes
back.

---

## Live prices

### The dashboard asks the provider directly. There is no cache in front of it.

No read-through, no fetch coalescer, no in-memory tier. Opening the dashboard fetches the caller's tickers
from Finnhub, on that request, and joins them to the holdings in memory.

That is deliberate and it is not a performance decision — it is a correctness one. **A cached stock price is a
wrong stock price.** Read-through checks the cache first and fetches on a miss, which means the normal answer
is the stale one and freshness is the exception; here the normal answer is fresh and the stored value is only
ever the failure path. Same components, opposite direction.

The practical consequence is the one a reviewer actually tests: a ticker added ten seconds ago has a price on
its **first** render. Nothing has to have polled it, warmed it, or invalidated anything.

There *is* a poller and a Redis price window, and they exist only because alert evaluation asks a different
question. "What is this worth now" needs one number and can be fetched on demand. "How has it moved over the
last N minutes" needs history, and history needs sampling. The proof the two are independent: with no alerts
configured anywhere, the poller wakes up, finds an empty target list and calls nothing, and the dashboard
behaves exactly the same.

### The fallback, and how stale is too stale

Every quote this app pays an API call for is written down, one key per ticker:

```
marketdata:last:{TICKER}  ->  "{price}:{epochMs}"
```

When the provider cannot answer for a symbol, that value is served instead, marked and rendered **with its
age**. The fallback is computed per ticker as a set difference — `requested − returned` — not per call. The
distinction matters exactly when the interesting failure happens: three of twenty tickers time out, and the
seventeen good prices must not be thrown away and replaced with twenty stale ones. Every row of the failure
matrix returns **200**; the table degrades, the request does not fail.

**The staleness call: count open-market minutes, not wall-clock minutes.** A last-known price is shown
while the market has been open for **an hour or less** since that price was recorded. Past that the price
column shows a dash instead, and the table says prices are unavailable.

A wall-clock cap is the obvious rule and it is wrong twice over. At 03:00 on a Sunday the last price is
Friday's close, which is the *correct* price — hiding it blanks the table of a perfectly healthy app. And
the same hole reopens every weeknight: at 03:00 on a Tuesday the last price is Monday's close, eleven hours
old, with the market shut the entire time. Counting only the minutes the market was actually open closes
both, because both are zero open minutes.

| When | Open minutes since that price | Result |
|---|---|---|
| Sunday 14:00, price from Friday's close | 0 | shown |
| Tuesday 03:00, price from Monday's close | 0 | shown |
| Tuesday 10:15, provider down since 09:30 | 45 | shown, amber |
| Tuesday 11:00, provider down since 09:30 | 90 | dashed |

**The counter-argument is a good one and survives.** A dash *does* recreate the blank table the fallback
exists to prevent, and that is the one thing a reviewer killing the provider will see. The answer is not
that it never happens — it is that it can now only happen after a full hour of **open market** with nothing
to show. That is a genuinely broken feed, which is a thing the screen should say out loud, rather than a
healthy Sunday afternoon. A dash on Sunday would be a lie; a dash at 11:00 on a Tuesday is the truth.

`TradingClock` gets the New York session from `TimeZoneInfo`, so real daylight-saving rules apply and
09:30–16:00 New York is 14:30–21:00 UTC in winter and 13:30–20:00 UTC in summer. **Holidays are not
handled** — see [Known gaps](#known-gaps).

Two other checks sit beside the age rule in `LastKnownPrice.IsWorthShowing`, and they are about whether the
**stored reading is sound** rather than about age: a price of zero or less is rejected (a corrupt write, and
exactly the shape a bad upstream response would leave behind), and a timestamp more than five minutes in the
future is rejected (a replica with a wrong clock).

Amber on a row means `isLastKnown`, never age. A fresh provider answer for a thinly traded symbol also carries
an old timestamp, and colouring on the timestamp would light the whole table up on a healthy Sunday — the
degradation signal firing on the happy path.

A ticker that has *never* been fetched has nothing to fall back to. That position still lists, with price and
profit/loss `null` rather than `$0.00`, and is excluded from the totals with a footnote. Zero is a claim; the
truth is "unknown".

**Every one of those states says which it is.** The amber banner names the reason — the quote provider is
not responding — rather than leaving the user to guess between a slow network and a dead feed. A row served
from the store is labelled *last known* with its age, so a stale price and a merely-late price no longer look
identical. And when nothing can be priced at all, the table keeps its columns, shows a dash in each of them,
totals the cost only, and prints a line saying prices are unavailable. **Never `$0.00`** — a test pins that,
because a made-up zero is the one failure mode that looks like data.

### Redis down changes nothing on the dashboard, and stops alerts dead

That asymmetry surprises people, so it is worth stating plainly. The dashboard asks the provider
directly on every load, so a cache outage costs it nothing at all — the prices are *fresher* than
usual, if anything, because the fallback is simply unreachable. What breaks is alerting: the sampled
price window lives in Redis, and without it there is no history to compare anything against.

So the panel says alerts are suppressed and no threshold is evaluated. **A stale price is a degraded
read; a made-up price history is a wrong alert.** Inventing history to keep the evaluator busy would
push notifications about moves that never happened, and a false alert about your money is worse than
no alert. The suppression clears by itself once the cache is back and one poll cycle has run.

A cache outage is registered as **degraded**, not unhealthy, so `/health/ready` still answers 200 and
the platform keeps the replica serving. The alternative was worse than the outage: readiness answering
503 would withdraw every replica and turn "alerts are paused" into "the API is unreachable".

### `dp` is not the number a threshold wants

Finnhub's `/quote` returns `dp`, a percent change, and it is tempting because it is already a percent. It is
change versus the **previous session close** — not versus your window, and not versus anything the user chose.
A "down 5% in the last 30 minutes" alert computed from `dp` fires on a stock that opened down 5% and has not
moved since, and stays silent on one that fell 5% in the last ten minutes from a flat open.

So `dp` is deserialised and ignored. Thresholds are computed from this app's own observations, which is the
entire reason the sampled price window exists.

### The free tier's ceiling, and what it means for two people looking at once

Finnhub has **no batch endpoint** — `symbol` is singular, so N tickers is N HTTP calls. Confirmed from their
machine-readable API spec: 105 paths, none of them plural.

The published limits, and how confident we are in each:

| Limit | Status |
|---|---|
| 30 calls/second burst cap, all plans; over-limit → 429 | **Confirmed** |
| 60 calls/minute on the free tier | **Inferred.** `finnhub.io` was unreachable from the environment this was written in, so the figure comes from a search snippet rather than from their documentation. Nothing contradicts it, and no other figure appeared |

Taking 60/minute as read, the arithmetic is unkind and worth stating plainly rather than hiding:

- Twenty positions is **twenty calls for one viewer, per refresh**.
- At a fifteen-second refresh that is four times as often, so **one viewer with twenty positions is already
  past sixty calls a minute on their own** — no second viewer required.

That is a property of the free tier, not a bug to engineer around — and it is not what the refresh interval
is sized against. The interval is a per-user preference, 60 seconds by default because that is a sensible
cadence for a stock dashboard regardless of which provider answers it, not because of any one provider's
quota. The interval control offers 15s / 30s / 60s / 5m and the faster options are the user's to spend.

What bounds the damage is not a quota counter. There was one — a process-wide token bucket sized to this
provider's free tier — and Phase 5 deleted it, because architecting around one provider's quota is exactly
what the brief says not to do. What paces the calls now is the provider's own back-off delay being honoured,
the circuit breaker, a fan-out capped at 4 concurrent requests, and per-ticker isolation. A symbol that comes
back refused falls back to its last known price, like any other failure.

### Adding a holding checks the symbol exists, and fails open

`POST /api/holdings` asks the provider whether the symbol is real. That puts an outbound HTTP call on a
**write** path, which is a trade worth naming: one extra call per add against a 60/minute budget is nothing,
but a Finnhub outage rejecting valid purchases would convert a degraded read into a broken write.

So the check returns **true when the provider cannot answer**. It is a separate interface from the price read
for exactly this reason — the two degrade in opposite directions, and one policy cannot serve both.

The check uses `/search` with an exact, case-insensitive match on the returned symbol. Not `/quote`: a
non-existent symbol and a healthy symbol Finnhub blipped on both come back as `c: 0`, so `/quote` cannot
distinguish them in principle and would mark a valid holding unknown after one bad second. Not `count > 0`
either — `/search` matches company names as well as symbols, so `q=appl` comes back with Applied Materials,
Applovin and Science Applications International beside Apple. A non-empty result says nothing about the
symbol you actually asked for.

### Finding a symbol you half-remember

The ticker field suggests companies as you type. It is still a text box first: type `AAPL` and submit
without ever opening the list, and nothing changes from before it existed. That matters because search
answers `200` with an empty list for *every* failure — a query too short to mean anything, a provider that
is down, a provider out of quota — so an outage looks like "no matches" and leaves a working form rather
than blocking a purchase someone really made.

Suggestions are filtered to symbols the add-position form would actually accept. `/search` returns foreign
listings such as `AAPL.SW` alongside `AAPL`, and offering one would fill the field with a value the form
then rejects. Fuzzy matching itself is kept: `appl` finds Apple, and also Applied Materials and Applovin,
because the provider matches company names as well as symbols.

Search is also the only thing that produces a **company name**, which then appears on the holdings and
dashboard tables. Names are cached in Redis for a week; prices never are. The rules only look contradictory:
a price is meant to change every second, so a stored one is almost certainly wrong, while a name is meant
never to change. The week-long expiry exists so that a company which renames itself corrects on its own. A
row with no cached name shows its ticker alone — the ordinary case for anything added before this shipped,
and for anything at all if Redis is down.

One cosmetic quirk, observed live and left alone: Finnhub does not return a company's name in a consistent
case. `q=appl` gives `Apple Inc` and `q=AAPL` gives `APPLE INC`, so which one a row displays depends on
which search happened to warm the cache. Normalising it would mean guessing at capitalisation rules for
every company in the world, which is a worse answer than showing what the provider said.

### No API key? It still works.

With no `Finnhub__ApiKey` configured the app registers `FakeQuoteProvider`, logs a single warning naming the
active provider, and serves a deterministic seeded random walk — stable per `(ticker, minute)`, continuous
within a UTC day, priced $20–$500 per symbol and identical across replicas and restarts. The provider's name
reaches a reader two ways, and both come from the same string the startup log prints, so the log and the page
cannot disagree: the anonymous `GET /api/marketdata/health` returns `{"provider":"…"}` and is what a deploy
or a curl checks without a credential, while **the SPA's health panel reads `provider` out of the
authenticated `GET /api/health/detail`** — one request that carries the databases, the cache and the feed as
well, instead of a second round trip for one word.

This is the clean-clone path and the test path, and it is why `docker compose up` needs no credentials. It is
**not** for the deployed app: leaving the fake on in Azure serves invented prices for real tickers, which
reads as broken rather than as a thoughtful fallback.

---

## Alerts

Set a percentage and a time window on a position — "tell me if AAPL moves more than 2% in fifteen
minutes" — and when it happens a row appears in the browser without a refresh. A threshold belongs to a
position, not to an account, so the set of rows in that table **is** the list of tickers anyone cares
about. Nothing has to ask the portfolio who is watching what, and the poller samples only those tickers.

### The false positive that had to be killed first

Measuring against the extremes of your own window — the highest and lowest price seen in the last N
minutes — catches real moves that comparing the two ends of the window sleeps through. It also fires
nonsense, and systematically.

**The worked case.** A stock opens the window at $150, dips to $141, and is now $149.

| Measurement | Figure | Direction |
|---|---|---|
| End to end — `(current − oldest) / oldest` | −0.67% | **down** |
| Against the window low — `(current − low) / low` | +5.67% | **up** |

Against the low alone that is a 5.67% **rise**, and the price is down over the hour. Worse, it is not a
one-off: any stock oscillating inside a band wider than the threshold fires on every single up-leg,
forever, held back only by the cooldown. A standing property of the window is being reported as an event.

**The rule chosen is sign agreement:** both measurements must point the same way before anything fires,
and only then is the extreme move compared against the threshold. In the case above they disagree, so
nothing fires. The alert reports the extreme move, because it is the larger and the one a person cares
about, and carries the end-to-end move beside it so the text can say what it was measured against.

Two alternatives were considered and rejected. **Recency** — the extreme must be recent — catches "fell
hard just now" and misses a slow grind away from an old extreme. **Current is the extreme** — only fire
at fresh window highs and lows — is very quiet and very defensible, and gives up exactly the case the
extremes were introduced for: opens $145, peaks $150, bottoms $141, now $142 is a −5.33% slide off the
high that end-to-end comparison (−2.07%) never sees, and sign agreement still fires it because both
measurements point down.

What sign agreement gives up honestly: a V-shaped recovery that ends net up reports as a large **rise**.
Oldest $150, low $130, now $151 is +0.67% end to end and +16.15% off the low; both point up, so it
fires at 16.15%. The climb from $130 is real and the text names the comparison, but it is the same
*shape* of artefact — the rule only kills the half where the two measurements disagree.

Three guards run before any comparison, and a cooldown after it. There must be enough samples, because
one stale point is not a window. The window must not straddle a period when nothing was sampled — a
Friday-close-to-Monday-open gap is not a sharp move. And a stale feed suppresses price alerts entirely,
because no new data must never read as "nothing moved". The cooldown is per user *and* ticker *and*
direction, so a drawdown cooldown does not mask the run-up that follows it.

### The push, and the two settings that make it work

Alerts travel server to browser, a few times an hour, over a SignalR hub at `/api/alerts/stream`. The
comparison with the hand-written version it replaced is under
[Decisions worth defending](#decisions-worth-defending). The shell badge says **"Live (WebSocket)"**,
because claiming a transport the code does not use is a self-inflicted wound.

Three things about it are ours rather than the library's, and each fails silently if it is wrong.

**WebSockets only, and no negotiation.** These are one decision, not two. The Redis backplane normally
requires a browser to keep reaching the replica it first connected to — sticky sessions. The documented
exemption needs *both* WebSockets as the only transport *and* skipping SignalR's negotiate round trip.
Allow a fallback transport and the requirement comes back without anything saying so: alerts then arrive
for some users and not others, only with more than one replica, only in production.

**The credential travels in the URL.** A browser cannot put an `Authorization` header on this kind of
connection, and the SPA and the API sit on different origins permanently, so a cross-origin cookie is
not dependable either — Safari blocks third-party cookies outright. SignalR's answer is to send the
access token as a query parameter, and the server reads it back from there. That read is scoped to the
hub's path: without the check, every route in the app would accept a credential in its URL, and every
access log would hold one. The browser refreshes the token inside the factory SignalR calls before each
attempt, because a reconnect after a long outage would otherwise present an expired one for ever.

**One claim decides who a message is for.** `Clients.User(...)` asks a provider for the id, and the
built-in provider reads a claim these tokens do not carry. With the wrong one, every alert is delivered
to nobody — no exception, no log line, nothing on screen. A test pins both that our provider is
registered and that it names the same claim the tokens are issued with.

Staying connected is no longer our problem. Azure Container Apps closes an idle request after four
minutes, and four is both the default and the floor on Consumption; SignalR's own keepalive runs about
every fifteen seconds, so the hand-written heartbeat that used to exist here is gone.

Fan-out across replicas is mandatory rather than an optimisation. An alert can be produced on one
replica while the user's stream is held by another, so every replica subscribes to a Redis channel per
user and whichever one holds the stream writes it out. Without it, alerts silently stop arriving for
half the users the moment there is more than one replica.

### There is no replay, deliberately

No cursor, no `Last-Event-ID`, no "the last 24 hours on connect". The alert is **written to the database
first and pushed second**, so whether anyone is connected only decides whether it also arrives right
now. The panel loads recent alerts with an ordinary request when it mounts, and refetches that query on
a dropped connection like any other. Anything missed while disconnected comes back that way, using
machinery the query layer already provides — a replay protocol would be a second delivery mechanism for
data the first one already has. The requirement asks for an event on breach, a background check and a
manual trigger; offline delivery is not in it.

**Simulate** exists because outside market hours nothing moves, so without it the feature cannot be
demonstrated at all. It picks one of your positions, synthesises a plausible move at your threshold, and
sends it through the **real** path — saved, then published, flagged as simulated and badged in the UI.
Not a fake push straight to the socket, which would prove nothing about the mechanism.

---

## Settings

Five things are configurable per user, and each is owned by the module that can enforce it — theme and
language by Identity, the refresh interval and which positions show by Portfolio, the alert threshold by
Alerts, and your own provider key by MarketData. There is no shared settings table: one would be a piece of
the system nobody designed and everybody writes to. The screen reads each section from its own route and
writes each back separately, so a refused API key cannot discard a theme change you made in the same visit.

### The theme is applied before the page paints

The choice lives on the server so it follows you between devices, but a fetch cannot happen before the first
paint. So a blocking inline script in `<head>` — ahead of any stylesheet — reads a browser-storage copy and
stamps `data-theme` on `<html>`. Without it every load flashes light before React mounts, on every
navigation. Browser storage is a bootstrap cache only; the server value wins once you are signed in.

Tailwind v4 has no config file, so dark mode is a `@custom-variant` line in CSS — and the palette variables
are a *second* place keyed off the same state. Changing only the variant leaves the app rendering the light
palette in both modes with no error anywhere.

"Follow the system" watches the OS preference and reacts live, so changing your laptop theme changes the app
without a reload.

### A faster refresh costs real calls

The interval is yours, between 10 and 300 seconds, 60 by default. Every refresh is one provider call per
visible position, so fifteen seconds makes four times the requests sixty does. Sixty is the default because
it is a sensible cadence for a stock dashboard, not because of any provider's ceiling.

### Hiding a position hides it, and nothing else

The dashboard table shows only the positions you have left visible, and the totals follow — what you see adds
up. **Alerts ignore visibility entirely.** You still own a hidden position, and a 6% drop still matters to
your money whether or not the row is on your screen, so a threshold on a hidden ticker still fires. This is
the first thing a reviewer asks, and there is a test that fails if anyone "fixes" it.

Hiding also has nothing to do with what gets polled. The poller samples tickers somebody has an active alert
on, not everything anybody holds — so a hidden position with no alert is never sampled, and the dashboard
prices it from the provider the moment you unhide it.

### Your own API key

Paste a Finnhub key and the app uses it for **your** dashboard fetches. The shared background poller behind
alerts keeps using the application's key, because the polled ticker list is shared — two people with an alert
on the same ticker would otherwise fetch it twice.

The key is validated with one live call before it is stored, so a bad key is refused while you are looking at
the field. "The provider refused your key" and "the provider could not answer" are different messages: telling
someone their key is wrong when the provider was merely down is its own kind of bug. If a stored key is later
revoked, the next fetch that sees a 401 marks it, and the settings screen tells you to re-enter it — otherwise
revocation is invisible and you just see stale prices.

It is encrypted at rest, and the encryption key ring is persisted to Postgres. The framework's default keeps
that ring in the container filesystem, and Azure replaces the container on every deploy, which would turn
every stored key into ciphertext nothing can read.

**The key is never returned to the browser.** Not to the person who set it, not masked beyond the last four
characters. The status response says whether one is configured and shows those four, and that is all — every
path that can return it is a path that can leak it.

Bring-your-own-key requests go out on their own HTTP client. Not for quota: the circuit breaker is shared, and
one user whose key is revoked would otherwise open it for everybody, including the poller.

### Language

English and Ukrainian. Numbers and dates use the chosen locale; the currency stays US dollars and only the
presentation localises. A build check fails if the two locale files disagree on a single key — there is no
fallback to English, because falling back hides a missing translation from whoever added it and shows it to
everyone else.

Server-generated text — API validation messages, a fired alert's reason — stays English. The backend does no
language negotiation.

## Testing

| Suite | Covers |
|---|---|
| `Shared.Kernel.UnitTests` | `Money` arithmetic and its currency checks |
| `Modules.Identity.UnitTests` | the `UserPreferences` entity, the appearance-settings request validator, and EF's constructor binding over that entity — password hashing is the framework's now, so nothing here tests it |
| `Modules.Portfolio.UnitTests` | the merge/correct rules, rounding, and the dashboard P&L calculator |
| `Modules.MarketData.UnitTests` | Finnhub response mapping, the fake's determinism, the Redis stores' encode/decode, the poller and its leases, the trading clock and the three-state feed verdict |
| `Modules.Alerts.UnitTests` | the sign-agreement rule, the three guards, the cooldown, and the entities behind them |
| `Architecture.Tests` | the six boundary rules, plus a test that the rules can fail |
| `Api.IntegrationTests` | Testcontainers Postgres + Redis, real HTTP, real migrations |

`dotnet test` runs all seven suites with Docker up; `npm --prefix src/Web test` runs the browser tests
separately. The exact counts are kept in one place only, [CLAUDE.md](CLAUDE.md).

**Nothing is skipped, and that took work.** `Identity.Contracts` is empty on purpose, because nothing
reaches into Identity. Two architecture rules used to *skip* over it — and a rule that skips enforces
nothing while still reporting green. They now generate no case at all for an assembly with no code in
it, so the count never includes a rule that is quietly asleep. Put a single type in that project and
both rules switch themselves back on with no edit anywhere. The exact list of assemblies no rule runs
over is fixed by its own test, so the filter can never become a hiding place.

Integration tests run the **same** `db/init/01-roles.sql` that ships, so the isolation under test is
the isolation that deploys.

The dashboard's degradation is tested rather than described: the provider wholly down, three of
twenty symbols failing, Redis down with the provider up, and a ticker that was never fetched. Every
one asserts **200**.

Graceful failure is tested the same way, and each of these can go red on the exact mistake it is named
after. A host is booted with Redis unreachable and `/health/ready` must answer **200** — 503 is what it
used to do, and it is what would withdraw every replica. A poll cycle with **zero** targets must report
*healthy*, because the naive version calls a brand-new deployment broken for ever. A unique-index
violation must stay **500** while a connection failure becomes **503**, so the merge race and a dead
database do not collapse into one answer. The alert stream must still be retrying after a seventh
failure, since the library's default gives up silently at the fourth. And a throw inside the alerts
panel must leave the dashboard standing, which a route-level boundary would not achieve — it would
replace the route.

The alert rule is tested the same way — by the thing it exists to prevent rather than by the thing it
does. A single "an alert fired at −6%" case passes under all three candidate rules and proves nothing.
What pins sign agreement is walking a price back and forth across the band six times and asserting three
alerts, not six; deleting the sign comparison turns that one red and leaves the single-shot cases green.

---

## Deployment

Three targets: `docker compose` (the P0 gate), **GitHub Pages** for the SPA, and **Azure Container
Apps** for the API. Postgres Flexible B1ms and Azure Managed Redis Balanced B0 — *not* Azure Cache
for Redis, which is retiring.

**Two of the three are live.** Phase 4 is deployed and verified on the public URL as of 2026-08-06:
the SPA at `dilicidum.github.io/StockPortfolio` renders live market prices against the ACA API, and
`/api/marketdata/health` returns `{"provider":"Finnhub"}` — a real key, not the fake. The API runs at
`stockp-api-qdgz3wugqbihs.icysea-481b5825.polandcentral.azurecontainerapps.io` in resource group
`stockportfolio-rg`. The measured burn was roughly $1.26/day while the API still scaled to zero; it is
higher now that a replica runs around the clock, and nobody has measured how much higher.
[docs/DEPLOYING.md](docs/DEPLOYING.md) says how to read the real rate and the live `deleteAfter` date —
both come from Azure, not from a document.

**Deploying is `git push origin main` and nothing else.** The full runbook is
[docs/DEPLOYING.md](docs/DEPLOYING.md) and the reasoning behind the cost model is in
[the design record](docs/superpowers/specs/2026-08-02-azure-deployment-design.md). Read the runbook
before touching Bicep or a workflow. Cost is bounded by **time, not budget**: pay-as-you-go has no
spending limit and a budget only emails, so the deploy stamps a `deleteAfter` tag on the resource
group and a scheduled workflow deletes the whole group once that date passes. Deploying extends the
window; not deploying lets it expire.

The SPA and the API sit on different origins and always will, which drives two things, and both are
now built: an explicit CORS policy in exactly one layer that allows credentials, and the alert hub
authenticating from the query string, because no browser can put a header on that connection. ACA's
`requestIdleTimeout` is 4 minutes and 4 is also the floor on Consumption; SignalR's keepalive covers it
without anything of ours running.

**The API no longer scales to zero.** `minReplicas` is 1, because the quote poller has to run between
requests and a sleeping replica evaluates nothing. That removes the cold start on the first request of a
session and adds one always-on container to the bill. The scale rule's concurrency threshold is 400
rather than 100 for a related reason: a held-open stream can count as one in-flight request for its
entire life, so at 100 a few dozen connected browsers would scale on user count rather than on load.
`maxReplicas` stays at 2, which is what the connection budget allows.

**Three probes, three different questions.** Liveness (`/health/live`) touches nothing at all — if it
checked Postgres or Redis, a brief dependency failure would become a container restart loop and turn a
degraded app into a down one. Readiness (`/health/ready`) runs the four database logins and the cache,
and answers a JSON body naming each component, so the deploy's smoke step can assert them one by one
instead of reading a single word. Startup (`/health/startup`) asks each context for pending migrations
and nothing else; that is a database round trip, which is exactly why it must never be the liveness
probe. There is also an authenticated `GET /api/health/detail`, which reports the same shape and always
answers **200** — a route whose job is to say Postgres is down cannot use that failure as its own reply.

Every connection string carries `Maximum Pool Size=2`: B1ms allows 35 user connections, and a
different username is a different Npgsql pool. What matters is **what opens a pool, not what is
defined**. The database creates five roles and four schemas, and the API registers exactly four
`DbContext`s — Identity, Portfolio, Alerts and, since Phase 5, MarketData — so there are **four pools per
replica**: 2 replicas × 4 pools × 2 = **16**, leaving 19 spare. `migrator` runs as a separate job, not
alongside the API. The Npgsql default of 100 would ask for 800. Count `AddDbContext` calls rather than
roles — this arithmetic has been published wrong before.

### Tearing it down

The resource group deletes itself. `deploy.yml` stamps `deleteAfter = today + 14` on
`stockportfolio-rg`, and `teardown.yml` runs daily at 03:00 UTC and deletes the whole group once that
date has passed. An unreadable or missing tag also deletes — deliberately, because a group with no
readable deadline is a group nothing is bounding.

To delete it **now**, run the Teardown workflow by hand with its `force` input set:

```bash
gh workflow run teardown.yml --repo Dilicidum/StockPortfolio -f force=true
```

Nothing else needs doing and nothing is kept, because there is no state outside the group. To bring it
back, push to `main` — the deploy provisions from empty.

---

## What we rejected, and why

The larger design arguments are above, each next to the thing it decided. This is the smaller list —
changes that were investigated properly and turned down, written here so nobody proposes them a second
time. The full register, including what is merely deferred, is
[docs/deferred-work.md](docs/deferred-work.md).

| Proposal | Why not |
|---|---|
| One shared generic value converter for the id types, instead of one per id | It has to build the id from a `Guid` *inside an expression tree*, and the interface member that would do it cannot be called there. The workaround is hand-built expression trees keyed on a property name as a string — trading fourteen duplicated lines for reflection that breaks on a rename, at startup. Worth revisiting at roughly eight more id types, and then with a source generator. |
| A `WithStandardProblems()` helper to stop every endpoint listing its statuses | The statuses genuinely differ per route: `/me` is a `GET` and rightly omits 415, `/register` omits 401 and is the only 409. Any blanket helper is wrong for at least one route, and it works against the rule that an endpoint declares exactly what it can emit. |
| Replacing the migrator's walk over registered services | The walk is correct and fails loudly at zero contexts. Every alternative is worse: a public migrate method per module means four coordinated edits every phase, and making the contexts public breaks the layering. |
| `EFCore.NamingConventions`, to delete eleven `HasColumnName` calls | Taking on a dependency to remove eleven explicit, self-explaining column names is a bad trade. |
| A market-holiday calendar for the dash rule | A week of work for a demo. The failure is cosmetic and self-correcting — see [Known gaps](#known-gaps). |
| A cached ticker table inside MarketData | The poll list is read live from Alerts each cycle. Removing the table also removed two event handlers, a reconciliation pass, and a way for two lists to disagree. |
| Alert replay — a cursor, message ids, a 24-hour backfill | The alert is written to the database before it is pushed, and the panel reloads its history from an ordinary `GET` on reconnect. A replay protocol would be a second delivery mechanism for data the first one already has. |

---

## Known gaps

Stated plainly rather than left for you to find.

- **Phases 5 and 6 are not deployed.** Both are green locally and in CI. What can only be checked
  against the public URL is unproven there — an alert arriving from the deployed API, a stream still
  alive after four minutes, a saved provider key still readable after a redeploy, and the startup
  probe passing on a real Container Apps revision.
- **`what-if` has never been read by a human.** `az` is not installed on the development machine.
  `ci.yml`'s **Bicep build** job compiles the templates and `deploy.yml` runs `what-if` in the runner
  immediately before deploying, so both run — nobody has compared the output by eye. Phase 6 adds a
  third probe and removes dead parameters, so this is the deploy where reading that output would
  actually tell you something.
- **Market holidays are not handled.** The dash rule counts open-market minutes from a fixed
  09:30–16:00 New York session, Monday to Friday, with real daylight-saving rules and no holiday
  calendar. So on Thanksgiving afternoon the price column dashes an hour into a market that was never
  open. A holiday calendar is a week of work for a demo, the failure is cosmetic, and it corrects
  itself the next trading day.
- **Nothing caps how many positions you can hold, and the provider has no batch endpoint.** One
  visible position is one HTTP call per dashboard refresh, fanned out four at a time, so both the
  latency and the call count grow linearly with the portfolio. Twenty positions at a fifteen-second
  refresh is already past sixty calls a minute for a single viewer. Nothing breaks — a refused symbol
  falls back to its last known price like any other failure — but a hundred-position portfolio would be
  slow and would spend the free tier in seconds.
- **One browser tab holds one of its six connections per origin, permanently.** The alert stream is a
  held-open WebSocket, opened once in the authenticated layout and never per component. Six tabs of
  this app on one origin is the practical ceiling before other requests start queueing behind it. The
  cleanup that closes the connection is also what keeps React's development mode from opening two.
- **The free tier is a ceiling, and nothing in the app models it any more.** The client-side token
  bucket that used to pace calls at sixty a minute was removed in Phase 5: it was sized to one
  provider's free plan and the brief says free-tier limits are not a problem here. What survives is
  what a normal client does — retry honouring `Retry-After`, a circuit breaker, per-ticker isolation,
  and a fall back to the last known price with its age. Over budget, tickers degrade rather than fail.
  A user who supplies their own key gets a separate outbound client, so a revoked key of theirs cannot
  open the breaker for everybody.
- **Signing out leaves a window of up to 15 minutes.** A session is an ASP.NET Core Identity bearer
  token — sealed and self-contained, with no row behind it — so there is nothing to delete and no way
  to retire one early. Logging out rolls the user's security stamp, which kills the *refresh* token
  immediately, but an access token already in a browser stays valid until it expires. Fifteen minutes
  is the host's setting and is the whole of that residual window; the refresh token's 14 days is the
  framework's default and nothing in the repo changes it.
- **A cache outage is degraded, not unready.** All four database logins are probed under their own
  names, which is what closed **C7**. Redis is probed beside them but registered as *degraded*, so
  `/health/ready` still answers 200 and the platform keeps the replica serving; only a database that
  is genuinely down gives the 503 that withdraws it. The alerts panel says alerts are suppressed
  instead of the API going unreachable.
- **The portfolio table has no price or profit/loss columns.** Those live on the dashboard, which is
  the screen that fetches prices. This is a decision, not an omission: adding them to the holdings
  table would make a CRUD screen pay the provider fan-out on every render.
- **Server-generated text stays English.** API validation messages and a fired alert's reason are
  produced by the backend, which does no language negotiation, so they do not follow the interface
  language. Everything the SPA itself renders does.
- **Npgsql logs `Cannot load library libgssapi_krb5.so.2`** in the container at startup. It is
  probing for Kerberos, falls back to password auth, and is harmless.
- **The Data Protection key ring is stored unencrypted.** Persisting it to Postgres (see "Your
  own API key" above) is correct and is what keeps a stored key readable across an Azure
  redeploy — but no certificate or key-vault protector is configured, so the master key
  material sits in plain form in the same schema as the ciphertext it protects, readable by
  the same database role. That protects a leaked dump of `user_provider_keys` **alone**, not
  a leak of the whole `marketdata` schema — anyone who can read the schema can read the key
  ring and decrypt every stored key with it. The fix is a certificate or key-vault protector
  on the key ring; neither is configured today.
