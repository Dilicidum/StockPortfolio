# Architecture

## Shape

The backend is a **modular monolith**: one ASP.NET Core process, with each module a separate project inside it. The word matters — a monolith is defined by its *deployment unit*, not its source layout. Several ASP.NET Core processes in one solution would be microservices in a monorepo, which is a different thing with a different cost.

Modularity here is not a staging post on the way to splitting. It pays for itself immediately in **change locality** (a bug in alerting is in the alerts module, so you reason about three thousand lines rather than sixty thousand) and **blast radius** (a refactor inside Portfolio *cannot* break Identity, because it cannot reach it). Extractability is a side effect, not the purpose.

Four rules make the boundaries real rather than aspirational:

| Rule | Enforced by |
|---|---|
| A module references only other modules' `.Contracts` projects, never their implementation | The compiler |
| Everything is `internal` except what sits in `.Contracts` | The compiler |
| Each module owns a Postgres schema and connects as **its own database role** | Postgres — a cross-schema query fails with a permission error |
| Cross-module reads go through contract interfaces; cross-module facts arrive as events | Architecture tests (NetArchTest) |

The third is the one that usually gets skipped. "Don't query another module's tables" enforced by code review decays within a fortnight. Enforced by `GRANT` it fails at runtime, in CI, on the first test run.

## The four modules

**Identity** owns users, password hashes, refresh tokens, and user preferences including alert settings. **Portfolio** owns holdings. **MarketData** owns the ticker list it polls and the price observations it collects. **Alerts** owns thresholds, fired alerts, and its own projection of who holds what.

They're named for domain concepts rather than mechanisms. Earlier drafts had *Auth*, *Settings* and *Notifications* — all three describe how something works rather than what it is about, and two of them weren't contexts at all. Settings are properties of a user, so they belong to Identity. Notification delivery is the tail end of alerting, not a bounded context of its own.

The dependency graph is worth reading, because it tells you the extraction order for free:

| From → To | Mechanism |
|---|---|
| Portfolio → MarketData | Synchronous, through a contract interface — the dashboard needs current prices |
| Portfolio → MarketData, Alerts | Events: holding added, holding removed |
| Anything → Identity | **Nothing at runtime** — the JWT is self-contained |

**Identity has zero inbound runtime coupling.** Nothing calls it during a request; the token already carries what anyone needs. That makes it the cheapest module to extract into its own process — a new host project, a Dockerfile, its own connection string, and one swapped DI registration. MarketData is the opposite: two modules depend on it, one synchronously, so extracting *that* would put a network hop on every dashboard render.

We're not extracting anything now. But the graph is why the answer to "should auth be a service?" is a reading of the structure rather than an opinion.

## Data

**One Postgres database, four schemas, four roles.** The isolation comes from the role, not from separate databases — each module's `DbContext` gets a connection string differing only in username, and `portfolio_svc` has no `USAGE` on the `identity` schema. Separate databases would only become right if you actually split into separate processes; within one process they'd cost you a single migration target and a single backup and buy nothing.

Two practical consequences. Migrations run as a **separate owner role** that can `CREATE`, while module roles get only DML — so the migration connection string differs from the runtime one. And because different usernames mean separate connection pools, four modules across three replicas at a pool of ten would exceed Postgres's default `max_connections` of 100. Set modest pool sizes deliberately, or you'll hit exhaustion under no real load and it'll look like a mysterious timeout.

**Prices live in Redis, not Postgres.** The justification is what the data *is*: price observations are derived and re-fetchable, so losing them costs alert history until the window refills — not user accounts, not holdings, not money. That risk profile is completely different from everything else in the system, and it's what licenses a different store. It also keeps the WebSocket migration a change of *writer* rather than a change of *schema*.

## Stack

| Choice | The reason |
|---|---|
| **EF Core**, `DbContext` per module | You know it, migrations are built in, and parameterisation is *unavoidable* rather than merely visible — which serves the task's most-repeated constraint better than raw SQL would |
| **PostgreSQL** | Schema-plus-role isolation is what makes the module boundaries physical |
| **Redis** | Price observations, plus the ingestion claim |
| **SSE**, not WebSockets or SignalR | The flow is server-to-client only. `Last-Event-ID` gives replay as a protocol feature rather than something to invent |
| **JWT issued in-house**, validated by framework middleware, **Argon2id** hashing | Own the business logic, don't reimplement the crypto. A hosted IdP would delete the thing being assessed |
| **`Microsoft.Extensions.Http.Resilience`** | Retry, circuit breaker and timeout in one line. Note `Microsoft.Extensions.Http.Polly` is deprecated |
| **Finnhub**, behind a provider interface | The only free option with a documented API and workable limits |
| React, TanStack Router + Query, Tailwind, **Radix primitives** | The no-UI-kit rule exists to stop you installing a theme, not to make you reimplement focus traps and keyboard navigation badly |
| xUnit, **Testcontainers**, NetArchTest | Tests run against real Postgres, so schema and SQL bugs surface in CI rather than being mocked away |

---

# MarketData

## The cycle

A hosted background service (`BackgroundService`) runs in **every API replica** on a **60-second** timer. Each cycle opens with a **claim** — a set-if-not-exists write to Redis keyed by the current time window, expiring after **120 seconds**. Exactly one replica wins; the rest skip until the next tick.

The key property is that nothing is held, released or renewed, and **no replica is special**. Leases have to be renewed and can be lost mid-work, which produces the zombie-leader problem where the old holder keeps working while a new one starts. Keying the claim to the window sidesteps that entirely: a crash or rolling deploy just means a different winner next cycle, and a skipped cycle is repaired by the following one.

## What gets polled

**Every distinct ticker anyone holds**, not filtered by who is currently online. Two reasons: every held ticker appears on somebody's dashboard, and alerts have to be evaluated for users who aren't logged in — an alert you only receive while already watching the screen isn't worth having.

MarketData knows the set from **its own table** of distinct tickers, maintained by subscribing to Portfolio's **holding-added** and **holding-removed** events. A ticker enters when the first person buys it and leaves when the last person sells. That avoids both a synchronous cross-module call and a cross-schema read, neither of which the module's database role would permit anyway.

The loop is **gated on trading hours**, which does the real cost reduction: US equities trade about a fifth of the week, so running around the clock would be five times the calls for prices that aren't moving.

## Fetching

**Finnhub has no batch endpoint** — one call per ticker. Against the free tier's **60 calls per minute**, that caps the system at roughly **50 distinct tickers across all users**. It's a hard wall and worth stating plainly, because a reviewer will ask and the honest answer is better than a discovered limitation.

Calls are spread across the cycle under a **token-bucket limiter** rather than fired as a burst, and a 429 is honoured by its `Retry-After` header rather than a guessed backoff — the provider is telling you when to come back.

The provider sits behind an interface taking a **set of symbols** and returning quotes that each carry **their own observation timestamp** rather than one stamped on the batch. Under polling that looks redundant, since every quote in a cycle is equally fresh. It exists for the WebSocket migration, where one symbol may be seconds old and another twenty minutes — and code that assumes batch-level freshness breaks silently the day you switch.

## Storage

One **Redis sorted set per ticker**, scored by observation time. Each entry combines **timestamp and price**, because sorted-set members must be unique: if the member were just the price, a ticker hitting the same value twice would update the existing entry's score rather than adding a new one, silently erasing the earlier reading.

Retention is **trimmed on the same write** — **1 hour 1 minute**, being the longest configurable alert window plus margin. No cleanup job. At this cadence that's **61 entries per ticker**.

The margin covers two things and nothing else: clock skew between the replica that trims and the one that evaluates, and the seconds an evaluation pass takes to work through every ticker after fixing its window boundary. A minute is thousands of times more than either needs. What actually matters is the guard — **retention must exceed the configurable maximum, validated at startup** — because otherwise someone raises the window in config, nobody updates retention, and alerts stop firing with no error anywhere.

Redis runs **append-only, flushed every second**, so a restart loses about a second of observations rather than the whole window. That matters more than it sounds: losing the window leaves alerts with nothing to compare against until it refills, which is up to an hour of silent blindness rather than a visible failure.

Redis being unreachable would otherwise take the dashboard's prices down along with ingestion, so it's covered twice — **replicated Redis** with automatic failover in production, and a **last-known-good price cache held in memory by each API replica**, served with the freshness timestamp doing the honest work.

---

# Portfolio

A portfolio is a **list of positions the user maintains by hand**. This is a tracker, not a broker — nothing is bought or sold in the app, so the holdings table records what the user *says* they hold rather than a ledger of transactions.

**One row per (user, ticker).** Adding a ticker already held merges into it: quantities sum and the purchase price becomes the **weighted average**. Ten shares at $100 followed by ten at $150 gives twenty at $125. **Remove deletes the row** — not a partial sell, because the task describes CRUD over a list, and partial disposals would drag in a transaction history it never asks for. That's the same complexity FIFO lot tracking would have introduced through the back door.

P&L then falls out directly. Per position: market value is quantity times current price, cost is quantity times average price, and profit is the difference — reported in **currency and percent**. Across the portfolio: total value, total cost, total profit, same two forms.

Everything is computed **server-side in `decimal`**. Floating-point money in the browser produces `$1234.5600000001`, and once it's in the client you can't fix it without touching every call site.

A ticker with no price yet — just added, so the poller hasn't reached it until its next cycle — returns **null and renders as pending**, never `$0.00`. A zero would flow into the totals as a complete loss on that position.

---

# Alerts

## The rule

**One threshold and one window per user**, applied to every ticker they hold. Not a per-ticker rules engine — the task describes a single user-configured threshold, and a rules table would be a richer product than was asked for.

The window is capped at **1 hour**. "Moved sharply" is a minutes-to-an-hour concept; a move over several days is a trend, which is a different feature.

## Evaluation

Runs **immediately after each fetch, in the same cycle**, for every user who holds that ticker and has alerts enabled — **online or not**. The natural trigger for "did this move sharply" is "a new price just arrived," so evaluating on any other schedule means either re-checking data you already checked or checking stale data.

Per ticker, three numbers are computed once from the Redis window — **current, minimum and maximum** — and every user's threshold tests against the same three. Two comparisons fire: current against the maximum (a drawdown) and current against the minimum (a run-up).

Using extremes rather than the window's endpoints is what catches a move that partly reversed. A price that falls from $150 to $141 and recovers to $149 reads as **-0.7% on an endpoint comparison** — invisible — but **+5.7% against the window's low**. The move was real; the endpoints just don't see it.

A fired threshold **stamps a cooldown**, without which it would fire every cycle for as long as the price stayed past the line. Three guards run first: enough samples in the window rather than one stale point, both ends inside the same trading session, and a stale feed suppresses price alerts entirely while raising a feed-health signal — **no new data must never read as "nothing moved."**

## Delivery

A fired alert is **written to Postgres first**; connection state only decides what happens next. If the user has an open SSE connection it's pushed immediately. If not, it simply sits there until they next connect — because **the SSE endpoint replays before it streams**.

The browser reconnects with the id of the last alert it saw (`Last-Event-ID`, which `EventSource` sends automatically), the server sends everything newer, and only then switches to live pushes. A fresh login with no id gets the **last 24 hours**. One mechanism covers both cases: a 40-second network blip and a three-day absence differ only in how many rows get replayed.

Replayed alerts show **where the price stands now** — *"AAPL fell 6% at 11:03, since recovered, now +0.2%"* — which needs no re-evaluation, only the trigger price stored alongside the alert and compared against the current one when rendering. A price alert is **a moment that passed, not a condition that persists**, and showing a four-hour-old drop as though it's still happening is misleading. So the UI is *recent activity*, not *active alerts*.

---

# Dashboard

**One request returns the whole view.** The client never fetches holdings and then makes a second call for quotes — that waterfall doubles latency for no benefit.

Portfolio loads the user's holdings from Postgres, asks MarketData for the newest entry per ticker through the contract interface, and joins them **in memory**. That in-memory join is the visible consequence of prices living in Redis: you can no longer sort or filter by current value in the database. At twenty holdings per user that costs nothing; it would matter at thousands of positions.

The response carries a **freshness timestamp**, so a provider outage or rate limit degrades to **visibly stale numbers** rather than a failed request. Per-position timestamps matter as well as the headline one, because a thinly traded ticker can be minutes staler than the rest and a single global figure hides that.

The browser refetches on the same **60-second** cadence with **refetch-on-window-focus**, so someone returning to the tab gets current data immediately instead of waiting out the cycle. Polling is right here because prices arrive on a known schedule — you already know new data exists every 60 seconds, so asking is never wasted and never late. Push is reserved for alerts, where a breach can happen at any second and polling for "did anything trigger?" is exactly the waste push exists to remove.

**Prices polled, alerts pushed — two transports because it's two kinds of data, not a compromise.**

---

# What we rejected, and why

Several of these were reversed during design, so the reasoning is worth keeping.

| Rejected | Because |
|---|---|
| **Separate auth service** | Deferred rather than refused. Identity is genuinely the best extraction candidate — JWT validation is offline, so there's no runtime coupling — but the cost is a second Dockerfile, migration runner, connection string, and health-check ordering, against a schedule where the hand-built frontend is the largest block. The seam is documented; decide when the frontend's real cost is known |
| **Dapper** | The parameterisation argument cuts the other way: EF Core makes injection structurally hard rather than merely visible. And you know it |
| **SignalR** | A bidirectional RPC framework with transport negotiation and a hub protocol, for a strictly one-directional problem |
| **Transactional outbox** | It closes a real gap — the process dying between committing an alert and publishing it. But the alert is already persisted, and the SSE cursor replays anything missed, so a lost publish costs latency rather than the alert. Worth building when an event causes something irreversible, like sending an email |
| **RabbitMQ / Service Bus** | With the outbox gone there's no durable-queue-shaped work left. It returns the moment you add external side effects that need retry and dead-lettering |
| **Separate worker process** | Overkill at this size. In-process plus the window claim is correct at N replicas, and extraction stays cheap because the work lives in a handler that doesn't know how it was invoked |
| **Container Apps Jobs on a cron** | No concurrency policy at all, so a run exceeding its interval silently doubles up. 15-30 seconds of cold start per execution for .NET, and at per-minute frequency the cost matches an always-on worker anyway |
| **Hangfire / Quartz** | Hangfire's recurring scheduler polls on a hard-coded minute boundary, which is the worst possible fit here and makes every replica contend for the same lock at `:00`. Both cost ~11 tables for one job |
| **Yahoo Finance** | Batched and effectively unlimited, but undocumented, no published rate limit, a cookie-and-crumb handshake, and a history of breaking without notice |
| **WebSocket ingestion, for now** | A stream can be held by exactly one process, which contradicts the multi-replica API. It removes the 50-ticker ceiling and is the documented scaling path, but it costs a dedicated ingester plus reconnect handling — Finnhub drops connections without close frames, so that means backoff with jitter, application-level heartbeats, and flushing the buffer on reconnect so the first tick isn't compared against 40-minute-old data |
| **Postgres price snapshots, a `latest_price` table, upserts** | All superseded by Redis sorted sets, which give retention by trimming and need no upsert at all |

# What it costs

The API must **keep a replica alive** or ingestion stops, rolling deploys briefly interrupt polling, and the poller can't be restarted or scaled apart from request serving. All three are cheap to fix later.

**The claim is the one piece that doesn't survive a WebSocket migration.** A stream can be held by only one process, so it would give way to a dedicated ingester, with the 60-second timer becoming a flush interval for an in-memory buffer rather than a fetch trigger. Everything else — the provider interface, the sorted sets, trimming, evaluation, replay, delivery — carries over unchanged.

# Still open

- **Extract Identity to its own process?** Decide at day 5 with real schedule data. Both options are defensible; the flat preference is to keep it modular and write the paragraph explaining the seam
- **A per-holding alerts on/off flag** — a weak lever, since one interested user keeps a ticker in the poll set regardless. Probably skip
- **Confirm Finnhub's free-tier specifics and response shape** before relying on field names

# Build order

P0 items gate acceptance, so they come first, and the frontend gets the largest single allocation because every component is hand-built.

1. **Skeleton** — solution, four module projects with contracts, compose (api, postgres, redis), architecture tests, CI
2. **Identity** — register, login, refresh, JWT middleware
3. **Portfolio** — holdings CRUD with weighted average
4. **MarketData** — provider interface, poller, claim, Redis storage
5. **Dashboard endpoint** — join, P&L, freshness
6. **Frontend** — routing, auth flow, dashboard, portfolio CRUD *(the largest block)*
7. **Settings** — theme, language, i18n
8. **Alerts** — threshold settings, evaluation, SSE with replay, manual trigger endpoint
9. **Finish** — resilience tests, README carrying these decisions and the 50-ticker ceiling, responsive pass

Steps 1-6 cover every P0 item. Step 8 is P1 and the headline feature, so protect time for it — and the **manual trigger endpoint** the task asks for is what makes it demonstrable outside market hours, when nothing streams and no threshold will ever breach on its own.