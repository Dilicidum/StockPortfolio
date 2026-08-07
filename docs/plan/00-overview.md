# StockPortfolio — Overview

This is the map. Read it first, then the file for the phase you are working in.

## What we are building

A stock-portfolio tracker, built against a seven-day take-home brief. A person registers, records the shares
they own, and sees what those shares are worth right now, what they paid, and whether they are up or down.
They can also set a threshold on a position — "tell me if this moves more than 3%" — and get told the moment
it happens, without reloading the page.

It is a modular monolith on .NET with a React single-page app in front of it. One process, one deployment,
but internally split into modules with real boundaries, so any one of them could be lifted out later.

**The acceptance gate is a fixed list**, and nothing else counts until it is met: sign-in that survives a
refresh, quotes fetched and cached on the client, client-side routing across several screens, full create /
read / update / delete on holdings, a dashboard with totals and profit-and-loss, database access that is
parameterised, and one command — `docker compose up` — bringing the whole stack up from a clean clone. Extras
add points. A missing gate item makes the extras worthless.

The brief left most technology choices open, which is what allows the module split below.

## The six phases

Each phase is vertical: screens, backend, tests and a deploy. The whole build is budgeted at roughly six
days. Phases 1 to 3 cover every gate item, so the gate is passed a little over halfway through.

| # | Phase | What you can do at the end | State |
|---|---|---|---|
| 1 | Sign in | Register and log in, locally and on the public URL | Built and deployed |
| 2 | My portfolio | Add, merge, edit and delete holdings | Built and deployed |
| 3 | Live prices & P&L | Dashboard with real prices, totals, profit and loss | Built and deployed |
| 4 | Alerts | Threshold alerts pushed live, and the price poller behind them | Built and deployed |
| 5 | Make it mine | Theme, language, refresh interval, thresholds, row visibility, bring-your-own API key | Built |
| 6 | Doesn't break | Visible, honest degradation when a dependency fails | Built |

One plan file per phase: [phase 1](phase-1-sign-in.md), [phase 2](phase-2-my-portfolio.md),
[phase 3](phase-3-live-prices.md), [phase 4](phase-4-alerts.md), [phase 5](phase-5-make-it-mine.md),
[phase 6](phase-6-doesnt-break.md). Phases with an implementation plan beside them keep it only while the
phase is in flight; where the two disagree, the implementation plan wins on build order and the phase plan
wins on intent.

A phase is done when it works in a browser and is deployed — not when its tests pass.

## The modules, and why they are where they are

Four modules: **Identity**, **Portfolio**, **MarketData** and **Alerts**. All four exist.

- **Identity** owns users, passwords and tokens. Nothing at runtime depends on it; the token it issues
  carries everything a request needs.
- **Portfolio** owns holdings — what you own, how much, and the average price you paid.
- **MarketData** owns everything to do with the quote provider: fetching a price, checking a ticker exists,
  the resilience policy around a third-party API. It depends on no other module.
- **Alerts** owns thresholds and the alerts that have fired. It asks MarketData for price history and asks
  Portfolio whether a ticker is one you actually hold.

**Boundaries are argued from extraction cost, not from subdomain labels.** The question for every seam is
whether it would survive becoming a network call, which decomposes into four: does anything need a database
transaction across it, is the chattiness bounded, can one side fail while the other keeps working, and is
there exactly one writer per table. The full reasoning is in [module-boundaries.md](../reference/module-boundaries.md).

Alerts was merged into Portfolio during Phase 2, and that merge was reversed before any code was written for
it. The merge argued that because a ticker means the same thing on both sides there is only one model, but
that inverts the rule: divergent language proves two models exist; shared language does not prove one does.
The concrete facts point the other way — a threshold and a fired alert never share a transaction with a
holding, no rule spans the three of them, they are written on a completely different trigger, and alerts can
be down while the dashboard renders perfectly.

Related, and settled: there is **no domain-event machinery**. The one event ever planned existed only to
clear a cooldown across the Portfolio/Alerts line, and a cooldown expires by itself. Deleting the event was
the fix; deleting the boundary was not.

Each module that stores anything gets its own database schema and its own database login, and all four now
do. MarketData was the exception for three phases, because everything it kept was one value per ticker in
Redis; per-user provider keys ended that, and its schema holds those keys and the key ring that encrypts
them. Its prices are still Redis only.

## Prices: two questions, two paths

This is the decision most likely to be misread, because an earlier design ran both through the same
machinery.

| Question | Who asks | How it is answered |
|---|---|---|
| What is this worth right now? | The dashboard, on load | Ask the provider directly, for the tickers this user holds |
| How has it moved over the last few minutes? | Alert evaluation | Sample on a timer, keep a short series in Redis |

Only the second needs history, so only the second needs a poller — and the poller only runs for tickers that
somebody has an active alert on. With no alerts configured anywhere, the cycle finds an empty list and calls
nothing, and the dashboard behaves exactly the same.

**The dashboard never reads the cache first.** It asks the provider, every load. This is the opposite of a
read-through cache, and the difference matters: when a fetch fails for one ticker, that ticker falls back to
the last price ever recorded for it, shown with its age, while every ticker that succeeded shows a fresh
price. A blanket fallback that threw away the good prices because one call failed would look identical in
most tests and be wrong.

Two separate things live in Redis and are deliberately not collapsed into one. The last known price is a
single value per ticker, written by whatever path last fetched it, never trimmed, and is the dashboard's only
fallback. The alert window is a trimmed series and is only meaningful while an alert exists. Merging them
would tie how far back the dashboard can degrade to a retention setting that belongs to alerts.

**A missing provider key is a supported state, not an error.** With no key the app generates plausible prices
and logs a warning, so a clean clone runs with no registration anywhere — which is what makes the one-command
gate item real. The deployed environment has a genuine key, because serving invented prices for real tickers
on a public URL reads as broken.

**Checking a ticker exists is a search, not a quote.** The provider's quote endpoint reports a healthy symbol
it briefly failed on identically to a symbol that does not exist, so the check uses symbol search with an
exact match on the returned symbol. If the provider cannot answer, the check passes — an outage at the
provider must never block someone recording a purchase.

## Money

Money is a decimal on the server and is sent to the browser as a string. Nothing about money is computed in
JavaScript — not totals, not profit and loss, not weights, not percentages. Floating-point drift in a
currency figure is the kind of bug nobody reports and everybody notices.

An amount always travels with its currency. Multi-currency portfolios are not in scope, but the shape does
not assume otherwise.

## Data access

Database access goes through the ORM, with no hand-written SQL anywhere. The brief only asks that queries be
parameterised; going through the ORM makes that structural rather than a habit, and the test suite proves it
by watching the commands that reach the database and asserting no user-supplied value is ever spliced into
one.

## The flow, end to end

1. Someone registers. The password is hashed with a memory-hard algorithm; they get a short-lived access
   token and a long-lived refresh token. The session survives a hard refresh, which was designed for from the
   start — the app restores the session before the router mounts, otherwise a refresh on a guarded page
   always bounces to the login screen.
2. They add a holding: a ticker, a quantity, a price paid. The ticker is checked against the provider's
   symbol search. Adding the same ticker twice merges into one position at a weighted average price.
3. They open the dashboard. The server reads their visible holdings, asks the provider for a price for each
   one, and computes market value, cost basis, profit and loss and portfolio weight — all server-side. Prices
   that could not be fetched come back as a last-known value with its age, flagged as such.
4. The browser keeps the dashboard fresh by refetching on an interval and when the window regains focus.
   These queries are cached client-side, which is also what keeps the deployed API warm during a session.
5. They set a percentage threshold on a position. A background poller samples the prices that thresholds care
   about, and when one breaches, the alert is written down and pushed to any open browser over a WebSocket.
   A simulate button forces one so the mechanism is demonstrable without waiting for the market.

## Where it runs, and what it costs

| Target | What runs there | Why |
|---|---|---|
| `docker compose up` | Frontend, API, Postgres, Redis | The gate item: the whole stack, locally, one command |
| GitHub Pages | The single-page app, static | Free, no container, API address baked in at build time |
| Azure Container Apps | The API only | One app, at most two replicas |

Three consequences, designed for from Phase 1:

- **The browser and the API are on different origins, permanently.** The API names the Pages origin
  explicitly in its cross-origin policy.
- **The live connection cannot use an authorization header**, because no browser can set one on this kind of
  connection and cross-origin cookies are being phased out. So the token travels in the query string, which
  is the real-time library's own answer to the same problem, and the server only reads it there for the one
  path that needs it.
- **Static hosting needs a fallback page** so that deep links into client-side routes resolve, and the app's
  base path has to come from the environment rather than being baked in, because the local compose build
  serves it from the root and the Pages build does not.

**Deploying means pushing to the main branch.** Nothing else. The pipeline builds the image, previews the
infrastructure change, applies it, and rebuilds the static app. Do not hand-run infrastructure commands
against the live group.

**Cost is bounded by time, not by a budget.** A budget alert can only send email; it cannot stop anything.
Instead, every deploy stamps a delete-by date on the resource group, and a scheduled job destroys the entire
group once that date passes. Deploying extends the window by using it. A group with no readable deadline is
also deleted, deliberately — an unbounded group is exactly what this is guarding against.

The burn measured through Phase 3 was about **$1.26 a day**: a small managed Postgres, a small managed Redis
with high availability off, a container registry, and an API that scaled to zero when nobody was using it.

**Phase 4 ends the scaling to zero**, and this is one decision rather than two. The poller has to run between
requests; a sleeping copy samples nothing, so no alert ever fires and nothing reports a fault. So the minimum
is one copy always running, and the bill rises by roughly the price of one small always-on container. What is
bought back is the cold start — until now the first request of a session paid for a container to start and
*then* for the fan-out of quote calls, one after the other.

The concurrency threshold the platform scales on had to rise with it. A held-open stream can count as one
in-flight request for as long as it lives, so at the old setting a few dozen connected browsers would have
scaled the app on how many people were watching rather than on how much work it was doing. The ceiling stays
at two copies, which is what the database connection budget allows.

Read the [deployment runbook](../DEPLOYING.md) before touching anything operational, and the
[deployment design record](../superpowers/specs/2026-08-02-azure-deployment-design.md) for the reasoning
behind the cost model and the shape of the infrastructure.

## Deliberately not built

Cut on purpose. Don't reintroduce without asking.

- **Alert replay.** No cursor, no backfill on reconnect. The brief asks for an event on breach, a background
  check and a simulate button. History is a plain fetch; reconnecting just refreshes it.
- **A watchlist.** The brief's "list of stocks" sits inside dashboard settings, so it means which of your own
  holdings appear on the dashboard — a visibility flag, not a second list of stocks you do not own.
- **A stored table of tickers to poll.** The list is read live from the alerts side each cycle. Storing it
  would add two event handlers, a reconciliation pass and a way for the two to disagree.
- **Trading-hours gating.** It existed to stop pointless polling out of hours. The poller now only runs for
  tickers with an active alert and the dashboard fetches on demand, so there is nothing left to gate.
- **A hand-written live stream.** Built once and deleted: the framework's own real-time library does the
  reconnection, the keep-alive, the cross-replica delivery and the header-less authentication that were all
  written by hand first. The readme carries the comparison.
- **Any third-party UI component library.** The brief bans UI kits. Everything is hand-built on Tailwind with
  native form controls.
- **Swapping to the generated-price provider when the real one rejects the key.** It would put invented
  numbers under real ticker symbols on the public site. The app starts anyway, says the key was rejected,
  and serves the last price it stored.
- **A market-holiday calendar.** A week of work for a demo, against a cosmetic failure that clears itself.

## Known gaps

- **Nothing since phase 4 has been deployed**, so phases 5 and 6 are proven locally and in CI and unproven
  against the public URL. A phase is done when it works in a browser *and* is deployed.
- **Market holidays are not handled.** A stored price ages by open-market minutes against a fixed weekday
  session, so on a public holiday the price column goes blank an hour into a market that never opened. The
  failure is cosmetic and corrects itself the next trading day.
- **The provider's rate limit is quoted from a search snippet, not from the provider's own documentation.**
  The readme says so where it quotes it.
- **Token lifetimes are provisional** and want a deliberate decision.

## Where to go next

| Document | What it holds |
|---|---|
| [module-boundaries.md](../reference/module-boundaries.md) | The criterion behind each module, what is in each, the places a boundary was deliberately not drawn, and who owns which piece of data |
| [bounded-contexts.md](../reference/bounded-contexts.md) | The context map: what *kind* of relationship each seam is. Where the two disagree, this one wins on the kind of relationship and module-boundaries wins on where the seam goes |
| [er-diagram.md](../reference/er-diagram.md) | The tables, what lives in Redis instead, and the indexes that carry weight |
| [module-interactions.md](../reference/module-interactions.md) | The dependency graph, what crosses each boundary, and the runtime sequences |
| [identity-contracts.md](../reference/identity-contracts.md) | Identity's frozen contracts |
| [DEPLOYING.md](../DEPLOYING.md) | The deploy runbook |
| [deferred-work.md](../deferred-work.md) | Everything decided and not yet done |

`docs/Initial.md` is the original architecture essay. Treat it as history: where it conflicts with these
plans, the plans win. Three things in it are known to be wrong — who owns alert settings, where the price
window lives, and the arithmetic in its worked alert example.
