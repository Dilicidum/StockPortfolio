# Phase 3 — Live prices and P&L

**Shipped, deployed and live.** The public API serves genuine market prices from Finnhub, and the hosted SPA
renders them.

## What the phase delivers

Log in, open the dashboard, see every position priced. For each holding: quantity, average buy price, current
price, market value, cost, profit in money and in percent, its share of the portfolio, and when the price was
observed. Above them, totals for value, cost and profit, and a headline freshness stamp. A ticker added ten
seconds ago is priced on its first render, because the request fetches it.

This covers the brief's dashboard, price and P&L requirements.

## Where prices come from

**The dashboard asks the price provider directly, every time it loads.** It does not check a cache first.
That is the opposite order from a normal read-through cache, and the reason is not performance. A cached
stock price is a wrong stock price. Freshness is the product.

Only when the provider fails for a ticker does the app fall back to the last price it recorded for that
ticker, and it shows that price with its age.

**The fallback is decided per ticker, never per request.** After the provider call returns, the app takes the
difference between what it asked for and what came back, and reads only the missing tickers from the store.
If three of twenty tickers time out, seventeen fresh prices are kept and three fall back. Wrapping the whole
call in one failure check would throw seventeen good prices away because one ticker failed. The
whole-provider-down case then falls out of the per-ticker rule for free, as the case where nothing came back.

The per-ticker rule needs a matching per-ticker error handler inside the fan-out. It catches the network and
resilience failures by name, never everything: a programming mistake in the response mapping must still fail
loudly. Without that handler the parallel loop cancels the rest of the work on the first failure, turning one
dead ticker into a blank dashboard — the exact inverse of the design.

**Anything the app pays an API call for gets written down**, so there is always something to fall back to.
One value per ticker in Redis: the price and the moment it was observed. Overwritten, never trimmed, never
expired.

Two rules about that write. It happens above the provider, not inside it — with no API key the fake provider
is the only one running, and a write buried in the real provider would leave the fallback store permanently
empty on the path that has to work from a clean clone. And it must never be able to fail the request: the
fetch already succeeded and the caller is waiting on prices. Fire-and-forget is not enough, because it still
surfaces connection and timeout errors at the call site, so the write is awaited inside a handler that logs
and swallows Redis faults.

The stored value is a plain string of price and timestamp — not JSON, not a hash. Two fields, no schema
evolution planned, a value you can read straight off the Redis CLI, and a read side that fetches many tickers
in one round trip. A value that fails to decode means "no last-known price", never an exception; a corrupt
entry must not break the dashboard at the exact moment the provider is already down.

**A stale price is always shown, with its age.** There is no staleness cap. A cap hides Friday's close at
three in the morning on a Sunday, when it is the correct answer; a cap by market session needs a trading
calendar this design deliberately does not have; and either cap recreates the blank table the fallback exists
to prevent. What does disqualify a stored price is corruption — a price of zero or less, or a timestamp more
than five minutes in the future, which means a skewed clock.

## What a missing price means

**A ticker with no price shows as blank, never as zero.** A zero would read as a total loss on that position,
and would flow into the totals as one.

Three arithmetic rules follow.

- A position's share of the portfolio excludes unpriced positions from the denominator, and an unpriced
  position's share is blank, not zero. Zero is a claim — "this is 0% of your portfolio". The truth is
  "unknown". Priced shares then sum to 100 within rounding, and the check carries that tolerance rather than
  forcing an exact hundred.
- **Total cost is summed over the same positions as total value** — priced ones only. If value excludes an
  unpriced position but cost includes what it cost, profit reports a loss on a portfolio that is up.
- **The observation time is when this app fetched the quote, never the provider's own trade timestamp.** That
  timestamp is the last *trade* time and freezes at Friday's close, so binding to it paints every weekend
  dashboard amber while the provider is perfectly healthy — the degradation signal firing on the happy path.
  The amber trigger is the fact that a price came from the fallback, not its age.

The headline "stalest observation" is the oldest observation across priced positions, and is blank when
nothing is priced. Same reasoning for the total profit percentage: on a dashboard where nothing could be
priced, "0.00" claims break-even at the moment nothing is known.

## Money on the wire

**Money is computed on the server in decimal and sent as text.** Sent as a number it becomes a double the
moment the browser parses it, and the precision computed server-side is destroyed at that boundary regardless
of what the browser does afterwards.

The same applies to percentages and portfolio shares. They are not money, but they are decimals, and a
decimal does not stop being a double because its units are percent. They cross as text too, formatted
server-side; the browser appends a literal percent sign and does no arithmetic.

One trap worth stating plainly: the API omits nulls by default, so every nullable field of the dashboard
payload opts out of that explicitly. Otherwise a missing price is *absent* from the JSON rather than null,
and anything that deserialises the response cannot tell the two apart.

## The provider

Finnhub, with these constraints. They were verified against Finnhub's machine-readable spec, their own
client and their issue tracker — their documentation site was unreachable when this was researched, so the
free-tier figure is inferred from a search result and is stated as inferred wherever it appears.

- **Sixty calls a minute on the free tier**, plus a thirty-calls-a-second burst cap on every plan. Over the
  limit is a 429.
- **No batch endpoint.** N tickers is N calls. Twenty positions is twenty calls for one viewer. At a
  sixty-second refresh that is a third of the minute's budget for a single viewer, and three concurrent
  viewers exhaust it. That is a genuine property of the free tier rather than a bug to engineer around — but
  it is why the refresh interval defaults to sixty seconds, and why changing that default is not free.
- Every field of a quote response is optional. A *missing* price must stay distinguishable from a price of
  zero.
- **An all-zero response means "no price this cycle"**, identical to a fetch failure. It does not mean the
  symbol is unknown: Finnhub returns all-zero for healthy symbols it briefly failed on.
- A bad or unentitled key surfaces as 401 *or* 403, both with the same body. Neither is ever retried.
- The percent-change field is change against the previous session close, not against any window you chose.
  It is read and ignored, and the README says why.

Calls go out with bounded concurrency plus one process-wide token bucket — twenty-five tokens replenishing at
one a second. The bucket must be shared by the whole process. A bucket owned by the HTTP client object is a
fresh bucket per resolution, which would cap one dashboard request and nothing across concurrent ones.
Raising the concurrency limit buys nothing, because the bucket serialises the excess anyway; you just hold
more sockets.

Retry, circuit breaker and timeout come from the standard resilience pipeline, but **its defaults are
decorative here**. The circuit breaker's minimum throughput ships at a hundred, so it could never trip on a
twenty-ticker dashboard; it is lowered. The options validator runs at startup and is fatal, so an
inconsistent timeout pair takes the host down at boot — which means it takes down `docker compose up`, the
acceptance gate. Retry-After is honoured by the resilience library by default, and that is a fact about the
library, not a promise from Finnhub; the client-side token bucket is what actually carries the limit.

**A fake provider is mandatory, not a convenience.** Finnhub shut its sandbox down in 2022 and issues no demo
key, so without a fake the stack cannot come up from a clean clone unless the reader registers for an API key
first — and coming up in one command is the acceptance gate. The fake is the default whenever no API key is
configured; a missing key is a *supported* state and must not throw. Startup logs one line naming the active
provider, and `/api/marketdata/health` returns the same name, so the log a reviewer reads and the panel they
see cannot drift apart. The fake generates a seeded random walk, deterministic per ticker and minute, and
deterministic *across processes* so two replicas serve the same price. A development-only hook at
`/api/dev/nudge` moves a ticker by a percentage for a while, which Phase 4 needs to make an alert fire on
demand; it is gated on the environment *and* on the fake being the active provider, never on authorisation.
A price-manipulation endpoint any signed-in user can reach in production is still a price-manipulation
endpoint.

## Does this ticker exist

Adding a holding checks that the symbol is real. **The price endpoint cannot answer that question.** It
returns zero both for a symbol that does not exist and for a real one it briefly failed on, so no reading of
that response can tell them apart. A price-based check would permanently reject a valid holding after one bad
second.

The check therefore uses the search endpoint, matching the symbol exactly and case-insensitively. Never
"did the search return anything": search matches company names as well as symbols, so searching for `appl`
returns Applied Materials and Applovin beside Apple. A non-empty answer is not an answer about the symbol.

**If the provider cannot answer at all, the answer is "assume it exists."** This puts an outbound HTTP call on
a write path. One extra call per add against the budget is nothing; a provider outage rejecting a purchase
someone really made is not, because it converts a degraded read into a broken write.

Since the fake accepts any well-shaped ticker by design, this path can only ever be exercised against the
real provider. It was, on the deployed API, once the key was set.

## How the module is put together

**The price module has no database** — no context, no migration, nothing of its own in Postgres. Everything
it persists is one Redis value per ticker. This is a deliberate and stated exception to the
one-database-per-module rule, because a rule with a silent violation reads as an oversight and the next
reader "fixes" it. An empty database context to satisfy the shape would buy a zero-table migration and a
bookkeeping row, for no behaviour. The dormant schema and role stay where they are; Phase 5's
bring-your-own-key table is what makes them real.

The rest of the shape:

- The provider port belongs in the application layer with every other port. In the domain it would make the
  domain the thing infrastructure implements, reversing the dependency direction the layering exists to
  enforce.
- **MarketData declares its own ticker type rather than reaching for Portfolio's**, which the module boundary
  rules forbid. The two sides meet as a plain string, canonicalised on both. The cost is two canonicalisation
  rules that can drift, and the drift is invisible — it shows up as a dashboard row that never matches a
  price, not as a compile error. Two guards: a test per module pinning lower case to upper, and an exact-match
  lookup, so a divergence surfaces as a visible miss instead of quietly matching the wrong thing.
- **Two contracts, not one.** Reading prices and checking that a symbol exists are separate, because they
  degrade in opposite directions: a price failure falls back to the last known value, an existence failure
  must fail open. One module-shaped interface would be a grab bag, and Phase 4's price-window methods would
  land on it and force Portfolio to recompile whenever Alerts' needs changed.
- **The dashboard route belongs to Portfolio, not MarketData.** It is a portfolio read that happens to need
  prices. The dependency edge runs Portfolio → MarketData; putting the route on the other side would invert it
  and make the price module read holdings.
- The P&L arithmetic lives in a pure calculator that takes rows, prices and a timestamp and returns the
  result. Not because purity is a virtue in itself, but because the repo has no fakes and no mocking library,
  so arithmetic buried inside a handler would have had nowhere to be tested.
- Holdings and prices are joined in memory. That is the visible consequence of prices not living in the
  database: you cannot sort or filter by current value in SQL. At twenty holdings it costs nothing; at
  thousands it would matter.
- The dashboard query returns its result directly, with no failure cases. An empty portfolio is a valid
  dashboard, and a user with no holdings never reaches the price module at all.

## The dashboard in the browser

The route fetches with a plain query, deliberately not a route loader. A loader failure takes the whole route
down with an error page, and what is being graded is *visible degraded state*, not a blank screen. A plain
query keeps the last good table on screen and shows the error beside it.

It refetches on a configurable interval, defaulting to sixty seconds, and on window focus, so someone
returning to the tab gets current data immediately instead of waiting out the cycle. The interval is a user
preference, not a mirror of anything server-side — there is no cycle to stay in step with, because each
request fetches for itself. Push is reserved for Phase 4's alerts, where a breach can happen at any second
and the client cannot know to ask.

A blank price renders as a dash with a pending tooltip, and is footnoted out of the totals. A row whose price
came from the fallback is amber, with its age. A row trailing the newest observation by more than the refresh
interval shows its own timestamp — a thinly traded ticker can be minutes behind, and a single headline figure
hides that.

## Not in this phase

**No background work of any kind** — no poller, no timer, no hosted service. Price *history* is what alerts
need, so the poller, the price window and its retention all belong to Phase 4. That poller will only poll
tickers with an active alert, so it is not a superset of the dashboard's fetches; the same ticker may be
fetched twice in a minute by the two paths, and removing that duplication would mean the dashboard reading
the alert window, which is read-through caching wearing a hat. With no alerts configured, nothing polls and
this phase's dashboard behaves identically.

That absence is also what keeps scale-to-zero correct for one more phase. The honest cost is a cold start:
the first request after idle pays container start and then the fan-out. The refresh interval and refetch on
focus keep the app warm for the rest of a session, so it is a first-load cost rather than a per-request one.

**Ticker discovery-search was not built.** Typing letters and picking from a list of matching companies is a
different feature from checking that one symbol exists, and only the latter shipped. It is tracked in
[deferred-work.md](../deferred-work.md).

Also not built, and not to be reintroduced without asking: read-through caching, an in-memory price tier, a
fetch coalescer, and any cached table of tickers to poll. Each existed to make the dashboard's trip through
alert infrastructure survivable, and the dashboard no longer takes that trip.

## Deployment

Phase 3 needed no infrastructure changes; everything the deployment required was already in place. The one
operational action was setting a real Finnhub API key as a repository secret. Empty means the public URL
prices real tickers from an invented walk, which reads as broken rather than as a thoughtful fallback. The
empty path exists for the clean-clone local stack and for the tests, and nowhere else.

## Reference

These describe the shape of the system rather than the order it gets built in. They live in `docs/reference/`.

- [Module interactions](../reference/module-interactions.md) — the two questions Portfolio asks the price module, and why they are separate.
- [Data model](../reference/er-diagram.md) — the one Redis key this phase writes, and why the price module has no database.
- [Module boundaries](../reference/module-boundaries.md) — why the price module depends on nothing.
