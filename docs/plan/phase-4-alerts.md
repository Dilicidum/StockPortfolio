# Phase 4 — Alerts

Threshold alerts: you say "tell me if this moves more than 2% in fifteen minutes", and when it does, a row
appears in the browser without a refresh. This is the last of the four graded requirements, and the one most
likely to be skipped for time, so it is scheduled fourth rather than last.

The phase is done when you can set a threshold, click **Simulate**, and see an alert in under a second — and
then push a price past a threshold for real and see the same thing happen without help.

---

## 1. Alerts is its own module

It is the fourth module. Nothing of it exists on disk yet; building the module is the first job of the phase.
It gets the same five layers as the others, owns its own database schema, and connects as its own database
user. The schema and the user already exist and have been sitting unused since Phase 2 — this is what makes
them real.

The reason it is a separate module is not that it speaks a different language from Portfolio. It speaks
exactly the same one: a ticker is a ticker on both sides. The reason is that nothing ties the two together.

- An alert setting and a fired alert never have to be saved in the same transaction as a holding.
- No rule spans a holding and an alert. Each of the three is complete on its own.
- They are written at different moments — a holding when you buy, an alert when a price moves.
- Alerts can be entirely broken and the dashboard still renders correctly.

Those four questions are the test every boundary in this codebase is argued from: would this seam survive
becoming a network call. This one would.

Alerts asks Portfolio exactly one question — does this user hold this ticker — and only when a threshold is
being created, so you cannot set an alert on something you do not own. Nothing depends on Alerts. It is the
leaf of the dependency graph, which is also why it is safe to build last.

**Thresholds belong to Alerts, not to Identity.** Identity keeps account data and display preferences.
Evaluation runs for users who are not logged in, so it cannot depend on anything arriving in a token, and it
must be able to read every threshold from storage on its own. This keeps Identity with no inbound runtime
calls from anywhere, which is the property that makes it the cheapest module to extract.

---

## 2. What a user configures

A threshold belongs to a **position**, not to an account: it is set per user *and* per ticker. Each one
carries a percentage, a time window, and whether it is on.

This is not a rules engine. One threshold, one window, one pair of directions per position. The brief asks
for a configurable threshold, not a query language.

The window is capped at an hour. "Moved sharply" is a minutes-to-an-hour idea; a move over several days is a
trend, which is a different product. The window is also rejected if it is longer than how much price history
is actually kept, so a user cannot configure a question the store cannot answer.

Keeping the settings per ticker has a second effect worth noticing early: the set of rows in that table
**is** the list of tickers anyone cares about. Nothing has to ask Portfolio who is watching what.

---

## 3. The poller and the price window

Alert evaluation needs a series, not a price. "Did this fall 5% in the last fifteen minutes?" cannot be
answered from one quote. So something has to sample repeatedly and keep what it sampled. That is the only
reason a background job exists in this application at all — the dashboard asks the provider directly and
needs none of it.

**It polls only tickers somebody has an active alert on.** If nobody has any alerts, the cycle finds an
empty list and does nothing, which is the right amount of work for an app nobody is watching. The dashboard
behaves identically either way.

The list is Alerts' own. MarketData declares that it needs a list of tickers to poll, and the host supplies
the adapter that fills it from Alerts. MarketData depends on nothing, and the dependency graph does not
cycle.

**Price history lives in Redis as a trimmed series per ticker**, one entry per sample, cut back on write to
the longest configurable window plus a little margin. This is deliberately *not* the single last-known price
the dashboard falls back on when the provider is down. Their lifetimes differ: a last-known price is wanted
for as long as somebody might look, a window is trimmed to an hour and only exists while an alert does.
Merging them would tie how far back the dashboard can degrade to how long alerts keep history. Turn alerts
off for a ticker and it has no window at all — which is exactly when the dashboard still needs a price.

Retention is checked against the maximum configurable window at startup, and refuses to start if it is
short. Without that check, somebody raises the window in configuration, nobody raises retention, and alerts
stop firing with no error anywhere.

Running on more than one replica needs two locks, not one. A claim key picks one winner *within* a minute. A
separate in-flight flag, not keyed to the minute, stops a cycle that overran from being joined by the next
minute's cycle on another replica — the first key says nothing across minute boundaries. Both carry expiries
as a backstop for a process that dies mid-cycle.

Whatever the poller fetches also updates the last-known price, and it must do that through the same single
writer the rest of the application uses. Two writers means the fake provider path and the real provider path
can record differently, and the fake path is what makes the whole stack work from a clean clone.

---

## 4. Evaluation

Evaluate immediately after each fetch, in the same cycle. A new price arriving is the natural trigger for
"did this move sharply". Any other schedule either re-checks data already checked or checks stale data.

Per ticker, compute current, minimum and maximum from the window once, then test every threshold on that
ticker against the same three numbers.

### The false positive to fix before anything else

Comparing against the window's extremes catches real moves that an endpoint-to-endpoint comparison misses.
It also fires nonsense. A price that falls from $150 to $141 and recovers to $149 is **down** over the hour,
but reads as a **+5.7% run-up** against the window's low. Worse, it is systematic: any ticker oscillating
inside a band wider than the threshold fires every single cycle, forever, held back only by the cooldown.
That is a standing property of the window being reported as an event.

Three workable constraints, and this phase has to pick one:

- **Recency** — the extreme must be recent. Catches "fell hard just now", misses a slow grind away from an
  old extreme.
- **Current is the extreme** — only fire at fresh window highs and lows. Very quiet and very defensible, but
  gives up the partial-reversal case that the extremes were introduced for.
- **Sign agreement** — the endpoint move and the extreme move must point the same way. Kills the oscillation
  loop, keeps genuine sharp moves, and is closest to what the requirement literally asks for.

Whichever is chosen, name the comparison in the alert text and put both figures in the payload. A vague
alert is why people turn alerts off.

### Guards, then cooldown

Three guards run before any comparison.

- There must be **enough samples**. One stale point is not a window.
- Both ends must be in the **same trading session**. A Friday-close-to-Monday-open gap is not a sharp move.
- A **stale feed suppresses price alerts entirely** and raises a feed-health signal instead. No new data must
  never read as "nothing moved".

Then a cooldown, held in Redis with an expiry, per user *and* ticker *and* direction — so a drawdown
cooldown does not mask a run-up that follows it. Expiry is the whole meaning of a cooldown, so a store with
native expiry is the right one; a table would need a cleanup job to do the same thing worse.

**There are no domain events, and none are being added.** One was planned, purely to clear a cooldown when a
holding was deleted. A cooldown expires by itself, so nothing needs telling. The worst case is one
suppressed alert if the user re-buys the same ticker inside the window. If eager clearing ever turns out to
matter, that is a fresh argument to make on its own merits.

---

## 5. Getting alerts to the browser

**Persist, then publish.** The alert is written to the database first and pushed second. Whether anyone is
connected only decides whether it also arrives right now. A failed push then costs nothing — the row is
there and the panel picks it up on its next history fetch.

Alerts are pushed over a **one-way server-to-browser stream**, not WebSockets. Two consequences follow from
that choice and neither is optional.

**A ticket handshake.** The browser cannot attach a login header to this kind of connection, and the SPA and
the API are on different origins permanently, so cross-origin cookies are not dependable. The page asks an
authenticated endpoint for a short-lived, single-use ticket and opens the stream with it. Thirty seconds,
deleted the moment it is used. A long-lived token in a query string ends up in access logs, browser history
and proxy logs; a spent thirty-second ticket does not meaningfully.

**A twenty-second heartbeat.** The hosting platform closes an idle request after four minutes, and four is
both the default and the floor on the plan being used — raising it costs more than the rest of the stack put
together. So the stream must send something every twenty seconds. The stream API has no comment mechanism,
so the heartbeat is a real named event that the client ignores. The same traffic keeps the connection above
the server's minimum response data rate, which applies to this kind of stream even though it does not apply
to WebSockets.

**Fan-out across replicas is mandatory, not an optimisation.** An alert can be produced on one replica while
the user's stream is held by another. Every replica subscribes to a Redis channel and whichever one holds
the stream writes it out. Without this, alerts silently stop arriving for half the users the moment there is
more than one replica.

**There is no replay and no backfill.** No cursor, no last-event id, no "the last 24 hours on connect". The
requirement asks for an event when a threshold is breached, background checking, and a manual trigger — not
offline delivery. Fired alerts are saved, the panel loads recent ones with an ordinary request when it
mounts, and the stream only ever pushes new ones. On a dropped connection the panel refetches its history
like any other query. Anything missed while disconnected comes back that way, using machinery the query
layer already provides.

### Simulate

The brief asks for a manual trigger explicitly, and it earns its place: outside market hours nothing moves,
so without it the feature cannot be demonstrated at all. It picks one of the caller's tickers, synthesises a
plausible alert, and sends it through the **real** path — saved and published like any other, flagged as
simulated and badged in the UI. Not a fake push straight to the socket, which would prove nothing.

The development-only price nudge from the previous phase drives a genuine evaluation-triggered alert and is
the better demonstration where it is available. It is gated to development and to the fake provider, so it
does not exist on the deployed site — which is why Simulate has to exist.

### Endpoints

```
GET   /api/alerts/settings
PUT   /api/alerts/settings
GET   /api/alerts?limit=50
POST  /api/alerts/stream-ticket
GET   /api/alerts/stream?ticket=…
POST  /api/alerts/simulate
```

Everything except the stream itself is bearer-authenticated; the stream is authenticated by the ticket.
Money in the payload is serialised as strings, like everywhere else.

---

## 6. Frontend

One stream connection for the whole application, opened once inside the authenticated layout, never per
component. A held-open stream permanently occupies one of the browser's six connections per origin, and
React's development mode will happily open two if the effect is not written to survive being invoked twice.

The browser's built-in reconnect cannot be used, because the ticket in the URL has already been spent by the
time it retries. So a dropped connection is closed, a fresh ticket fetched, and a new connection opened with
backoff. Losing free reconnection is the price of header-less authentication.

Two places show alerts: the panel on the dashboard, which is the mockup's right-hand column, and a
notifications screen showing the same data with a longer history. Both are titled around *recent activity*
rather than *active alerts*, and every row carries a timestamp — a price alert is a moment that passed, not
a condition that persists, and the wording should not imply otherwise.

The live indicator in the shell says "Live (SSE)". Consistency between what is claimed and what was built is
graded, and labelling this a WebSocket is a self-inflicted wound.

---

## 7. Infrastructure

**The always-on replica setting has to change with this phase.** Until now nothing ran between requests, so
scaling to zero was free. A background job changes that: a sleeping replica evaluates no alerts. The two go
together, and one without the other is a feature that silently stops working whenever traffic does.

The scaling rule needs attention too. A held-open stream may count as one in-flight request for its entire
life, so with the default concurrency setting a few dozen connected browsers would scale on *user count*
rather than on load. Raise the concurrency threshold and keep the replica ceiling at two, which the database
connection budget requires anyway. A replica with an open stream also never qualifies for the platform's
reduced idle billing rate, so budget at the active rate. Adding this module adds a third connection pool per
replica — the budget still fits, but the arithmetic moves whenever a context is added, and it has been
published wrong before.

The poll interval and the retention window become configuration, in both compose and the deployment
template. The previous phase shipped neither, because there was nothing to configure.

Two things not to do. Never add response compression anywhere in the application — it wraps the body in a
buffering stream and the feed dies with no error. And in the compose stack, confirm the reverse proxy's
buffering is genuinely off for this route; with buffering on, events queue up and nothing arrives until the
response ends, which for a stream is never.

Finally, the architecture rules pin how many module assemblies exist, and that number changes when this
module lands. Move it one assembly at a time so the suite is green after each step rather than red for the
whole phase.

---

## 8. Done when

- Set a threshold, click Simulate, and an alert appears in the panel in under a second with its badge.
- Reload the page and the alert is still listed — from history, not replay.
- Simulate with the tab closed, then open the app: the alert is in the list.
- Leave a tab open for five minutes and the connection is still alive.
- Nudge a price past a threshold in the local stack and a real, evaluation-driven alert fires.
- Nudge twice inside the cooldown and only one alert arrives.
- Nudge back and forth across the threshold repeatedly and alerts stay bounded rather than one per cycle.
- With no alerts configured anywhere, nothing is polled and the dashboard is unchanged.
- The notifications screen lists history; the shell badge reads "Live (SSE)"; the panel is usable at 375px.
- Alerts arrive on the deployed site from the deployed API, and the stream survives past four minutes.
- The alerts schema is reached by the alerts database user, with its own migration history table — sharing
  one history table across contexts corrupts all of their bookkeeping.
- The README records: why a one-way stream rather than WebSockets, the ticket handshake and why it exists,
  the heartbeat and the four-minute platform limit, why replay was dropped, and which false-positive
  constraint was chosen and why.

## Reference

These describe the shape of the system rather than the order it gets built in. They live in `docs/reference/`.

- [Module boundaries](../reference/module-boundaries.md) — the full argument for Alerts being its own module.
- [Module interactions](../reference/module-interactions.md) — every designed edge on that diagram terminates in Alerts, and this phase builds them.
- [Data model](../reference/er-diagram.md) — the alert tables and the price-window key, all of which arrive here.
- [Bounded contexts](../reference/bounded-contexts.md) — what kind of relationship each new boundary is.
