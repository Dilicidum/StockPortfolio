# Phase 6 — Doesn't break

## What you can do at the end

Kill the quote provider and the app keeps working: it shows the last price it saw, says how old that price
is, and the health panel goes amber. Kill Redis and fresh prices still render — Redis only holds the fallback
— while alerts are suppressed and say so. Kill Postgres and you get a clear error with a retry button, not a
white screen.

This covers the brief's error-handling requirement end to end, and the grading criterion about handling
errors and edge cases.

**This is also the buffer phase.** If an earlier phase overran, the time comes from here. Cut in this order:
polish on the bring-your-own-key screen, then the Postgres-down path, then the responsive re-pass. Never cut
the provider-down path — it is the one a reviewer will actually trigger, by leaving a bad key in their
environment file.

---

## The four things that break

### The quote provider is down

Phase 3 already retries, times out and trips a circuit breaker. What this phase adds is what happens
**after** all of that gives up.

| Failure | What the user gets |
|---|---|
| Rate limited | The provider's own back-off delay is honoured. One warning is logged, not one per symbol. |
| Circuit open | No call is made at all. Every ticker falls back to the last price recorded for it, flagged as a last-known value with its age. There is no cache tier to read instead — the fallback *is* the cache. |
| Timeout or server error | Retried, then given up on. Prices already fetched in that request stay fetched. |
| Nonsense response | Logged with the raw body at debug level, that symbol skipped, every other symbol unaffected. One bad ticker must never take down a whole load. |

The property that matters: **a provider outage degrades the numbers, it never fails the request.** The
freshness timestamp does the honest work, and this phase makes sure the request actually reaches the point
where a timestamp can be shown, instead of being replaced by a server error.

### Redis is down

Redis holds two things: the last known price per ticker, and the short price history alerts need. Losing it
affects those two very differently.

- **Provider up, Redis down → the dashboard renders fresh prices and nothing visible changes.** All that is
  lost is the ability to degrade gracefully later. This is worth stating clearly because the obvious
  assumption is the opposite.
- **Redis down → alerts are suppressed entirely**, the feed reports itself degraded, and the alerts panel
  says so rather than sitting silently empty.

That asymmetry is deliberate and belongs in the README: a stale price is a degraded read, but a fabricated
price history is a wrong alert. Never invent history to keep the alert evaluator busy.

**Do not build an in-process fallback under Redis.** An earlier design had a last-known-good cache in each
replica's memory. Its failure mode is silent inconsistency — with two replicas, two users see different
prices for the same ticker, and every restart empties it. The stored last-known price does the same job,
shared across replicas, surviving restarts, and written by whatever path last fetched. A tier beneath it
would be a cache for a cache.

One connection setting is load-bearing: a Redis client must be configured not to abort on a failed first
connect, or a blip at startup leaves it permanently broken even after Redis comes back, and the app looks
like it never recovers. The application's own client already sets it. **The live stream opens a second,
separate connection for its cross-replica fan-out, and that one is configured by nothing** — so the setting
has to be applied there too, or the stream is the one part that never recovers.

**A cache outage must not take the container out of the load balancer.** Readiness runs every health check,
and a check that reports unhealthy on a Redis failure means every replica is withdrawn — turning "alerts are
suppressed" into "the whole API is unreachable", which is the exact opposite of what this section asks for.
The cache reports *degraded*, and degraded keeps serving traffic.

### Postgres is down

Nothing clever. Health reports unhealthy, the API returns 503 with a readable problem body, and the SPA shows
a full-page retry state. Transient blips are retried automatically; a genuinely down database is not
something to paper over.

⚠️ With automatic retries enabled, any explicit transaction must be run through the retry strategy, and the
work inside it must be safe to run twice — the strategy re-runs the whole block on a transient failure.

### The alert stream drops

**There is no replay.** No cursor, no resumption identifier, no backfill on connect — that protocol was
deliberately not built. When the connection comes back, the stream hook invalidates the alerts query and the
ordinary history request returns whatever fired while the browser was disconnected. The user sees the gap
filled without touching anything; the machinery doing it is the same query cache everything else uses.

A thin "reconnecting" bar, driven by the browser's online state and the stream's connection state, is the
only UI this needs.

---

## Making the state visible

An authenticated health-detail endpoint, separate from the unauthenticated endpoints the platform probes,
reports one entry per component — database, cache, quote feed — plus the last successful poll, the age of the
oldest usable observation, how many tickers are being tracked, and which provider is in use.

There is no figure for how much of the provider's rate allowance is left, because nothing counts it any more:
the client-side token bucket was deleted in Phase 5 as an answer to a problem the brief says is not a problem.
A number nothing measures is worse than no number, so the row is absent rather than blank.

**A health endpoint never answers 503 because the database is down.** It answers 200 carrying a body that
says so. An endpoint whose whole job is to report a failure cannot use that failure as its own status, or the
panel goes blank at the moment it becomes useful.

**Three states, not two.** Degraded is the whole point of this phase: the quote feed is degraded when the
last successful poll is older than it should be but there is still something to serve, and unhealthy only
when there is nothing left. A binary up-or-down hides exactly the state this phase exists to expose.

Naming the provider is deliberate. Someone running without an API key should be able to *see* that the fake
provider is active rather than wonder why the prices look synthetic.

**Two clocks, not one.** The dashboard's freshness is per request — was this price just fetched, or is it a
last-known value, and how old. The alert feed's freshness is time since the last successful poll. They are
different signals with different periods and must not be classified off a single clock.

The feed-health signal is written by MarketData's poller and has exactly one reader: the health detail. It is
tempting to feed it to the Alerts evaluator as well, and that would be a step backwards — the evaluator
already refuses to fire on a window with a silence inside it, per ticker, which is a sharper answer than one
number covering every ticker at once.

**No poll targets means healthy, not silent.** With nobody's alert set anywhere, the poller takes its lease,
reads an empty list and does nothing. A signal built around "did we store any prices" calls a brand-new
deployment broken for ever, and nothing would contradict it.

---

## What the browser does

**Three visible states, and none of them is a zero.**

| State | What it looks like |
|---|---|
| Fresh | Normal. "Updated 12s ago." |
| Stale | Amber banner naming the reason — the quote provider is not responding — with the last known numbers still in the table, dimmed. |
| Unavailable | The table keeps its structure with a dash in the price columns, totals show cost only, and an explicit note says prices are unavailable. **Never $0.00.** |

**Never retry a client error.** Retrying an unauthorised response three times burns the refresh window;
retrying a rejected input is pointless. Only server errors and network failures are retried.

**A failed dashboard fetch must not unmount the route.** Keep the last good table on screen with an inline
banner. This is the reason Phase 3 avoided loading the dashboard in the router — an error there replaces the
whole screen instead of annotating it.

**Error boundaries per route, plus one at the root.** A crash in the alerts panel must not take the dashboard
down. Each boundary offers a retry that resets the query rather than reloading the page.

**Mutation failures roll back and explain themselves in place.** Adding, editing or deleting a position that
fails restores the previous value and shows the server's message inline on the form. Not a toast — the brief
grades error handling, and an error you have to catch within three seconds does not count as handled.

⚠️ This is where an incorrect optimistic-rollback signature bites hardest, because it only shows up when a
mutation genuinely fails. Phase 2 has it covered; re-verify it here with the provider actually down.

The health panel polls its own endpoint on its own schedule, independent of the dashboard query, so a
dashboard failure does not take the health panel down with it.

---

## Probes and startup

**Three probes with genuinely different meanings**, because the platform acts on them differently:

- **Liveness — is the process alive.** It must not touch Postgres or Redis. A dependency blip that fails
  liveness gets the container killed and restarted in a loop, turning a degraded app into a down one. This is
  the highest-consequence mistake available in this phase.
- **Readiness — can it serve traffic.** Database reachable. Failing takes it out of the load balancer; it
  does not kill it, and it must therefore answer only the question it is named after — a cache outage is not
  an inability to serve traffic, and treating it as one withdraws every replica.
- **Startup — migrations applied, configuration valid.** Generous failure threshold, so a cold start is not
  mistaken for a crash.

All three must be declared as HTTP probes in the infrastructure definition. Container Apps injects plain TCP
probes when you don't, and the whole split silently becomes decoration. Compose gets the same split, and the
API restarts unless deliberately stopped.

**Startup validation fails fast, with a message that says what to fix**: every connection string present and
parseable, and the alert history retention at least as long as the longest configurable alert window. There
is nothing to check about a token signing key — sessions are issued as opaque tokens the framework protects,
not as signed ones, so no key material is read anywhere.

⚠️ **A rejected provider key is reported, not swapped away.** Which provider is used is decided when services
are registered, before any call can be made, so a key can only be found bad later. Do not answer that by
falling back to the fake provider: the deployed site would then price real tickers from a random walk because
a key was mistyped, and that is the one thing the fake provider must never do. Instead, the first rejection
raises a flag, the quote feed reports itself unhealthy with the reason, and the dashboard falls back to
last-known prices exactly as it does for any other outage. The standing rule from Phase 3 survives intact:
**a missing key is a supported state and nothing here may throw** — an eager failure takes down the
one-command startup, which is a gate item.

A container that refuses to start with a clear reason beats one that starts and serves nothing.

---

## Proving it

Tests pin the behaviour, but this phase is judged in a browser, so the proof has to be reproducible by hand.
Script it in the README:

```bash
docker compose stop redis      # dashboard still renders fresh prices; alerts suppressed and say so
docker compose start redis     # recovers within one cycle
docker compose stop postgres   # 503 and a retry screen — and the API is NOT restart-looping
```

Alongside those, three more done by hand: set a deliberately invalid provider key and restart; block the
provider mid-session; and force an error inside the alerts panel. The expected outcome of each is in the
checklist below.

Watch the container list as well as the browser. A restart loop looks fine from the outside for the first few
seconds.

The deploy pipeline gets a smoke step for the same reason: after deploying, the health detail must report a
healthy database, and the alert stream must produce a heartbeat within thirty seconds. Fail the deploy if
not.

---

## When the UI stops trusting a price

This is the phase's whole point — honest degradation. These numbers are literally what a reviewer sees when
they inevitably run it with a bad API key.

**Amber after twice the refresh interval.** The user already chooses that interval, so the warning scales
with what they asked for: two minutes at the default, twenty seconds for someone watching closely, ten
minutes for someone who is not. One fixed number would either cry wolf on every slow moment or sit silent
through a real outage, depending entirely on a setting it knows nothing about.

**A dash after an hour of open market.** Not an hour of wall clock — an hour in which the market was actually
trading. Friday's close is the correct, only and freshest possible answer all weekend, and it is equally
correct at three in the morning on a Tuesday; a plain time-since-last-price rule calls a healthy app broken in
both cases, and the second one is the one that is easy to forget. Counting only trading minutes answers both
with one rule.

That does mean the table can go blank, which is the thing the fallback exists to prevent. It can only happen
after an hour of open market with no price at all — a genuinely broken state, and one that says so rather
than presenting hours-old numbers as though they were current.

**Holidays are not handled.** On Thanksgiving afternoon the table dashes an hour into a closed market. A
holiday calendar is a week of work for a demo, the failure is cosmetic and corrects itself the next trading
day, and Phase 4 rejected a market calendar on the same grounds.

**The feed's own clock is separate, and counts poll cycles.** Degraded after three missed, unhealthy after
ten, both measured in multiples of the configured interval rather than in fixed seconds, so changing the
interval cannot silently change what "late" means.

Remember the two clocks: the dashboard is classified per request from the age of what it served, and the
alert feed from the time since its last successful poll.

---

## Done when

- Redis stopped — the dashboard still renders prices, the API stays in the load balancer, and the alerts
  panel says alerts are suppressed. Started again — it recovers within a cycle and the banner clears.
- An invalid provider key plus a restart — the app **starts**, logs a warning, keeps serving last-known
  prices, and the health panel says the key was rejected.
- Provider blocked mid-session — amber banner within twice the refresh interval, the table keeps the last
  good numbers, no server error anywhere in devtools.
- Postgres stopped — 503 with a readable message and a retry button, and the API **not** restart-looping.
- An error forced inside the alerts panel — the dashboard stays up.
- A failed position mutation — the row reverts to its correct previous value, message inline on the form.
- Offline, then back online — the reconnecting bar appears and clears, and the gap fills with no manual
  refresh. Not a replay: the stream carries only new events and the history request fills the gap.
- Backend and frontend test suites green.
- Deployed, and the provider-down case repeated against the live API.
- A clean-clone startup brings up every database schema **in use** — four schemas and every login actually
  connected by something, rather than one created and orphaned. The alerts schema, its login and its password
  are Phase 4's plumbing and stay exactly where they are; the deferred item they belong to closes by being
  consumed, not by being deleted.
- The README's *Checking the whole thing by hand* passes end to end, locally and deployed. That is the only
  checkable walkthrough there is, it already carries the provider-down case, and this phase adds the Redis
  and Postgres cases to it.
- **README complete.** The brief asks for a short description, so roughly a page plus a link to the
  architecture essay:
  - How to run it with one command, from a clean clone, with no API key.
  - The real-time transport decision, with its comparison table.
  - **Evidence of parameterised database access** — state that everything goes through the ORM with no
    hand-written SQL, then show it rather than claim it: one generated statement with its placeholders beside
    the parameter values, and a description of the interceptor that watches every command in the test suite
    and asserts no user-supplied value is ever spliced into one.
  - The fake provider, and why it is the default.
  - How a user's own API key is used, and where the application's is still used.
  - The Azure deployment, what it costs, and how to tear it down.
  - A trimmed "what we rejected, and why" table — the single best evidence for the grading criterion about
    justifying decisions.
  - Known limits: the ticker ceiling, the browser's six-connections-per-origin cap, and the free tier's
    roughly sixty calls a minute against twenty calls per dashboard load — stated as inferred, since the
    provider does not publish it.

## Reference

These describe the shape of the system rather than the order it gets built in. They live in `docs/reference/`.

- [Module interactions](../reference/module-interactions.md) — what still works when each dependency fails.
- [Data model](../reference/er-diagram.md) — what is in Redis and therefore lost when Redis is.
