# Phase 4 — Alerts

**Built, and not yet deployed.** Everything below is in the code and green locally. The two conditions that
can only be checked against the public URL — an alert arriving there, and a stream still alive after four
minutes — are still open.

Threshold alerts: you say "tell me if this moves more than 2% in fifteen minutes", and when it does, a row
appears in the browser without a refresh. This is the last of the four graded requirements, and the one most
likely to be skipped for time, so it is scheduled fourth rather than last.

The phase is done when you can set a threshold, click **Simulate**, and see an alert in under a second — and
then push a price past a threshold for real and see the same thing happen without help.

---

## 1. Alerts is its own module

It is the fourth module, with the same five layers as the others, its own database schema, and its own
database user. The schema and the user had been sitting unused since Phase 2; this is what made them real.

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

**That takes two declared needs, not one, and the second is easy to miss.** Evaluation runs immediately after
each fetch, in the same cycle, and evaluation belongs to Alerts — so the poller has to *tell somebody* a fresh
sample landed. Written the obvious way, MarketData calls Alerts, which is the one edge the graph forbids. So
MarketData states two needs of its own: which tickers am I to sample, and here is a sample that landed. The
host adapts both. Neither is worded as "ask Alerts", so if either answer ever comes from somewhere else, only
the host's adapter changes.

Putting the poller in the host instead would have looked tidier and does not work: it needs the rate-limit
bucket, the provider registration and the window store, all of which are internal to MarketData. Putting it in
Alerts and having Alerts pull prices is worse — it would hand Alerts the rate limit, the resilience policy and
the last-known-price write, and the last of those must have exactly one writer.

**Price history lives in Redis as a trimmed series per ticker**, one entry per sample, cut back on write to
the longest configurable window plus a little margin. This is deliberately *not* the single last-known price
the dashboard falls back on when the provider is down. Their lifetimes differ: a last-known price is wanted
for as long as somebody might look, a window is trimmed to an hour and only exists while an alert does.
Merging them would tie how far back the dashboard can degrade to how long alerts keep history. Turn alerts
off for a ticker and it has no window at all — which is exactly when the dashboard still needs a price.

Retention is checked against the maximum configurable window at startup, and refuses to start if it is
short. Without that check, somebody raises the window in configuration, nobody raises retention, and alerts
stop firing with no error anywhere.

**That check lives in the host, and it has to.** Retention belongs to MarketData; the cap on what a user may
configure belongs to Alerts. Nothing inside either module can see both numbers, and passing one module's
configuration into the other's registration would manufacture a dependency out of a number. The host reads
both values and compares them. It is wiring, not a feature — it owns no types and knows nothing but two
settings.

Running on more than one replica needs two locks, not one. A claim key picks one winner *within* a poll
cycle. A separate in-flight flag, not tied to a cycle, stops a cycle that overran from being joined by the
next one on another replica — the first key says nothing across cycle boundaries. Both carry expiries as a
backstop for a process that dies mid-cycle.

**Name the claim key after the cycle, never after the clock.** Naming it by the calendar minute was the
first shape, and it is wrong as soon as the interval is not a minute: the key's lifetime is a multiple of the
poll interval, so below thirty seconds the claim expires inside the very minute it names and a second replica
claims that minute again. The two only agree at the default interval, which is exactly the setting under
which nobody notices.

Acquire the claim first, then the in-flight flag, and release only what was actually acquired. Releasing
after a *refused* claim deletes the winner's in-flight flag and re-opens the overlap the second lock exists
to prevent.

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

Three constraints were workable, and **sign agreement is the one chosen**: the end-to-end move and the
extreme move must point the same way before anything fires, and only then is the extreme move compared
against the threshold. The extreme move is what the alert reports, because it is the larger and the one the
user cares about; the end-to-end move travels beside it so the text can say what the number was measured
against. A vague alert is why people turn alerts off.

The two rejected constraints, so they are not re-proposed:

- **Recency** — the extreme must be recent. Catches "fell hard just now", misses a slow grind away from an
  old extreme.
- **Current is the extreme** — only fire at fresh window highs and lows. Very quiet and very defensible, and
  gives up the partial-reversal case that the extremes were introduced for.

Sign agreement kills the case above: $150 to $141 to $149 is −0.67% end to end and +5.67% against the low,
the two disagree, and nothing fires. It keeps the case the extremes exist for: opens $145, peaks $150,
bottoms $141, now $142 is only −2.07% end to end, which no sensible threshold catches, but −5.33% off the
high — and both point down, so it fires. That is a real slide off a window high that an end-to-end
comparison sleeps through.

**What it gives up, stated honestly, because it is the same shape of artefact.** A V-shaped recovery that
ends net up reports as a large rise: oldest $150, low $130, now $151 is +0.67% end to end and +16.15% off
the low, both up, so it fires at 16.15%. The climb from $130 is real and the wording names the comparison,
but the rule only kills the half where the two measurements disagree. What stays silent is the *fall*, not
the alert. Three ways to settle it, none blocked and none chosen: accept it, since a 16% climb off the low is
information; require the end-to-end move to clear some fraction of the threshold too, which starts to
converge on the end-to-end-only comparison extremes exist to beat; or report the end-to-end move as the
headline and reference the extreme. **No test pins this case, deliberately** — a test would have settled a
product question silently.

### Guards, then cooldown

Three guards run before any comparison.

- There must be **enough samples**. One stale point is not a window.
- The window must not **straddle a period when nothing was sampled**. A Friday-close-to-Monday-open gap is
  not a sharp move.
- A **stale feed suppresses price alerts entirely** and raises a feed-health signal instead. No new data must
  never read as "nothing moved".

**The second guard is a gap check, not a market calendar.** A calendar means holidays, half-days and time
zones — a week of work for a demo — and it answers a narrower question than the one that matters. The
property actually wanted is that the window is not spanning a silence, and that is visible in the samples
themselves: the longest interval between two adjacent readings, compared against a small multiple of the poll
interval. A weekend gap is rejected; a cycle that missed two polls is not. This is strictly better than a
calendar for the failure it protects against, because a calendar says nothing about the provider having been
unreachable for an hour on a Tuesday.

**The feed-health signal is a log entry, and that is all it is for now.** It must not mean "send the user a
different kind of alert" — somebody who set a price threshold did not ask to be told about the data
pipeline. Nor can it be a count on the price module's health endpoint, which was the first idea and is
unbuildable as stated: only Alerts can judge whether a window is stale, and MarketData depends on nothing. So
a stale window suppresses that ticker's alerts and logs once per cycle at warning level. Turning that into
something a person sees is Phase 6's job, along with the rest of the degradation UI.

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

Alerts are pushed over **WebSockets, using the framework's own real-time library**, which is the technology
the brief names. The library owns the parts that are easy to write badly — reconnection, keeping an idle
connection open, and carrying a message to whichever replica holds the connection. Three consequences
follow, and none of them is optional.

**WebSockets are the only transport, and the negotiation step is skipped.** These are one decision. Carrying
messages between replicas through Redis normally requires each browser to keep reaching the replica it first
connected to. The documented exemption from that needs both halves — a single transport and no negotiation —
so allowing a fallback transport quietly brings the requirement back, and alerts then arrive for some users
and not others as soon as there is more than one replica.

**The credential travels in the URL.** The browser cannot attach a login header to this kind of connection,
and the SPA and the API are on different origins permanently, so cross-origin cookies are not dependable
either. The library's own answer is to send the access token as a query parameter; the server reads it back
from there, and only for the hub's path. Without that path restriction every route in the application would
accept a credential in its URL. The browser renews an expiring token inside the callback the library invokes
before each attempt, because a reconnection after a long outage would otherwise present a dead one for ever.

**One claim decides who a message is for.** Addressing a message to a user asks a provider for that user's
id, and the built-in provider reads a claim these tokens do not carry. With the wrong claim every alert is
delivered to nobody, and nothing fails, logs or appears on screen. This is the single most dangerous thing
in the feature and it is covered by a test that names the claim.

**Fan-out across replicas is mandatory, not an optimisation.** An alert can be produced on one replica while
the user's connection is held by another. This is one line of configuration rather than a component of ours,
and without it alerts silently stop arriving for half the users the moment there is more than one replica.

**There is no replay and no backfill.** No cursor, no message ids, no "the last 24 hours on connect". The
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
POST  /api/alerts/simulate
```

The alert feed is not in that list because it is not an ordinary route: it is the real-time library's own
endpoint, mounted at `/api/alerts/stream`, and it carries no documented status codes or response shape of
its own. Every route above is bearer-authenticated by a header; the feed is authenticated by the same token
in the query string, for the reason above.

Money in the payload is serialised as strings, like everywhere else — **and the threshold the user saves is
a plain number, which is not a contradiction.** The rule that survives is narrower than "percentages are
strings": a figure the *server computes* goes out as a string, because that is where precision is lost when
a browser parses it; a value the *user typed* arrives as a number, because the host rejects a quoted number
outright and the server is about to parse it into a decimal either way.

---

## 6. Frontend

One connection for the whole application, opened once inside the authenticated layout, never per component.
A held-open connection permanently occupies one of the browser's six per origin, and React's development
mode will happily open two if the effect is not written to survive being invoked twice.

Reconnection is the library's, with a retry schedule that never gives up — the default one stops after four
attempts, which would leave a tab silently disconnected after a short outage with nothing ever trying again.
When a connection comes back, the panel refetches its history rather than replaying anything.

Two places show alerts: the panel on the dashboard, which is the mockup's right-hand column, and a
notifications screen showing the same data with a longer history. Both are titled around *recent activity*
rather than *active alerts*, and every row carries a timestamp — a price alert is a moment that passed, not
a condition that persists, and the wording should not imply otherwise.

The live indicator in the shell names the transport that is actually in use. Consistency between what is
claimed and what was built is graded, and naming the wrong one is a self-inflicted wound.

---

## 7. Infrastructure

**The always-on replica setting changed with this phase.** Until now nothing ran between requests, so scaling
to zero was free. A background job ends that: a sleeping replica evaluates no alerts, and nothing anywhere
reports a fault. The minimum is one. The two are one decision, not two — reverting either alone leaves a
feature that silently stops working whenever traffic does.

The scaling rule moved with it. A held-open stream may count as one in-flight request for its entire life, so
at the old concurrency threshold a few dozen connected browsers would have scaled the app on *user count*
rather than on load; it is now four times higher. The replica ceiling stays at two, which the database
connection budget requires anyway. A replica with an open stream also never qualifies for the platform's
reduced idle billing rate, so budget at the active rate.

This module adds a third connection pool per replica: three registered contexts, a pool cap of two, two
replicas, so twelve of the tier's thirty-five. The budget still fits. Count the registrations rather than the
database logins — there are more logins than contexts, and this arithmetic has been published wrong before.

The poll interval and the retention window are configuration now, in both compose and the deployment
template. The previous phase shipped neither, because there was nothing to configure. One caution learned
here: a cap value must carry its real number in configuration, not a zero placeholder. Configuration beats
the code default, so a zero written for tidiness rejects every window a user could ask for.

Two things not to do. Never add response compression anywhere in the application — it wraps the body in a
buffering stream and the feed dies with no error. And in the compose stack, confirm the reverse proxy's
buffering is genuinely off for this route; with buffering on, events queue up and nothing arrives until the
response ends, which for a stream is never.

Finally, the architecture rules pin how many module assemblies exist, and that number changed when this
module landed — from seventeen to twenty-two. Moving it one assembly at a time kept the suite green after
each step rather than red for the whole phase, and it exposed something worth keeping: while a layer is
empty, the rules over it *skip*, and a skipping rule enforces nothing. The number of skips rose before it
fell. Do not read a green rule over an unpopulated layer as evidence of anything.

---

## 8. Done when

All of these hold locally. The one still outstanding is the deployed-site line: Phase 4 has not been
deployed, so neither an alert arriving there nor a stream surviving four minutes there has been seen.

- Set a threshold, click Simulate, and an alert appears in the panel in under a second with its badge.
- Reload the page and the alert is still listed — from history, not replay.
- Simulate with the tab closed, then open the app: the alert is in the list.
- Leave a tab open for five minutes and the connection is still alive.
- Nudge a price past a threshold in the local stack and a real, evaluation-driven alert fires.
- Nudge twice inside the cooldown and only one alert arrives.
- Nudge back and forth across the threshold repeatedly and alerts stay bounded rather than one per cycle.
- With no alerts configured anywhere, nothing is polled and the dashboard is unchanged.
- The notifications screen lists history; the shell badge names the real transport; the panel is usable at
  375px.
- Alerts arrive on the deployed site from the deployed API, and the connection survives past four minutes.
- The alerts schema is reached by the alerts database user, with its own migration history table — sharing
  one history table across contexts corrupts all of their bookkeeping.
- The README records: why the framework's real-time library rather than a hand-written stream, where the
  credential travels and why, which claim decides who a message is for, why replay was dropped, and which
  false-positive constraint was chosen and why.

## Reference

These describe the shape of the system rather than the order it gets built in. They live in `docs/reference/`.

- [Module boundaries](../reference/module-boundaries.md) — the full argument for Alerts being its own module.
- [Module interactions](../reference/module-interactions.md) — every edge terminating in Alerts, all of which this phase built.
- [Data model](../reference/er-diagram.md) — the alert tables and the price-window key, all of which arrived here.
- [Bounded contexts](../reference/bounded-contexts.md) — what kind of relationship each new boundary is.
