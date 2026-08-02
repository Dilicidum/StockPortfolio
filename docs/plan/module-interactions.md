# Module interactions

Four modules in one ASP.NET Core process. A module references only other modules' `.Contracts` projects; everything else is `internal`. The compiler enforces it, and `Architecture.Tests` asserts it.

---

## 1. Dependency graph

```mermaid
flowchart TB
    Web["Web — React SPA<br/>GitHub Pages"]
    Host["Api host — composition root<br/>endpoints · DI · PollSetAdapter"]

    subgraph Id["Identity"]
        IdC["Identity.Contracts"]
        IdI["Identity.Application · Domain · Infrastructure"]
    end

    subgraph Al["Alerts"]
        AlC["Alerts.Contracts"]
        AlI["Alerts.Application · Domain · Infrastructure"]
    end

    subgraph Pf["Portfolio"]
        PfC["Portfolio.Contracts"]
        PfI["Portfolio.Application · Domain · Infrastructure"]
    end

    subgraph Md["MarketData"]
        MdC["MarketData.Contracts"]
        MdI["MarketData.Application · Domain · Infrastructure"]
    end

    SK["Shared.Kernel — Money · CQRS interfaces · InvalidInput"]

    Web -->|HTTPS + SSE| Host
    Host --> IdI
    Host --> PfI
    Host --> MdI
    Host --> AlI
    Host -.->|"adapts Portfolio → IPollSetSource"| MdC

    AlI -->|"who holds this ticker"| PfC
    AlI -.->|HoldingRemoved| PfC
    AlI -->|"price window: current / min / max"| MdC
    PfI -->|"IQuoteReader — dashboard needs prices"| MdC

    IdI --> SK
    PfI --> SK
    MdI --> SK
    AlI --> SK

    style IdC fill:#2d4a3e,stroke:#4ade80,color:#e8f5e9
    style PfC fill:#2d4a3e,stroke:#4ade80,color:#e8f5e9
    style MdC fill:#2d4a3e,stroke:#4ade80,color:#e8f5e9
    style AlC fill:#2d4a3e,stroke:#4ade80,color:#e8f5e9
    style SK fill:#3a3a52,stroke:#818cf8,color:#e8eaf6
```

Solid arrows are synchronous calls through a contract interface; the dotted one is a domain event.

The graph is a clean line: **Alerts → Portfolio → MarketData**, with Identity off to the side.

**MarketData depends on nothing.** It needs to know which tickers to poll, but it declares that need as its own interface — `IPollSetSource` in `MarketData.Contracts` — and the host supplies a ten-line adapter backed by `Portfolio.Contracts`. Dependency inversion, and it is what keeps the graph acyclic. Without it, MarketData reading holdings directly would make Portfolio and MarketData mutually dependent, which quietly undermines the extraction-order argument the whole shape rests on.

**Nothing points at Identity at runtime.** The JWT is self-contained, so no module calls Identity during a request. That makes it the cheapest module to extract into its own process later — a new host project, a Dockerfile, its own connection string, one swapped DI registration. MarketData is at the other end: two modules depend on it, both synchronously, so extracting *that* would put a network hop on every dashboard render.

Nothing is being extracted now. The graph is why the answer to "should auth be a service?" is a reading of the structure rather than an opinion.

`.Contracts` projects hold records of primitives only — no EF Core reference, no aggregate types, no strongly-typed IDs. A contract carrying `UserId` would drag a value converter and a persistence concern across the boundary, so contracts use raw `Guid`.

### What crosses each boundary

| From → To | Mechanism | Carries |
|---|---|---|
| Host → MarketData | `IPollSetSource` adapter | `SELECT DISTINCT ticker` across all holdings |
| Portfolio → MarketData | `IQuoteReader` | Latest price per ticker, for the dashboard join |
| Alerts → MarketData | `IPriceWindowReader` | Current / min / max over the user's window |
| Alerts → Portfolio | `IHoldersOfTicker` | Which users hold a ticker, for evaluation |
| Portfolio → Alerts | `HoldingRemoved` event | Clears any pending cooldown for that user + ticker |
| Anything → Identity | **Nothing at runtime** | The token already carries what anyone needs |

---

## 2. Runtime — the poll cycle and an alert

```mermaid
sequenceDiagram
    participant T as PeriodicTimer<br/>(TimeProvider)
    participant P as QuotePollingService
    participant R as Redis
    participant F as IQuoteProvider<br/>Finnhub or Fake
    participant E as Alerts evaluator
    participant DB as Postgres
    participant S as SSE endpoint
    participant B as Browser

    T->>P: tick (60s)
    P->>R: SET claim:{window} NX EX 120
    R-->>P: acquired
    P->>R: SET cycle-inflight NX EX 110
    R-->>P: acquired
    Note over P,R: two keys — the window claim picks WHO,<br/>the in-flight guard prevents overlap

    P->>DB: IPollSetSource — SELECT DISTINCT ticker
    P->>F: GetQuotes(symbols) — token-bucket spread
    F-->>P: quotes, each with its own timestamp
    P->>R: ZADD prices:{ticker} + trim to 1h01m

    P->>E: evaluate(ticker, current, min, max)
    Note over E: guards — enough samples,<br/>same session, feed not stale
    E->>R: GET cooldown:{user}:{ticker}:{dir}
    E->>DB: INSERT fired_alerts
    E->>R: SET cooldown … EX
    E->>R: PUBLISH alerts:user:{id} {payload}

    R-->>S: message (any replica holding the stream)
    S->>B: event alert
    B->>B: setQueryData → panel updates

    P->>R: DEL cycle-inflight
```

The alert row is written before it is published, so a publish that fails leaves a record rather than nothing. But there is **no cursor and no replay** — an alert fired while nobody is connected is simply not pushed. It shows up next time the panel loads its history, because that is a plain `GET`, not a protocol feature.

Cooldown lives in Redis with a TTL. Expiry is the whole semantics of a cooldown, so a store with native expiry is the right one; a table would need a cleanup job to do the same thing worse.

---

## 3. Runtime — dashboard with read-through

```mermaid
sequenceDiagram
    participant B as Browser
    participant Q as GetDashboard handler
    participant PG as Postgres (portfolio)
    participant MD as MarketData
    participant R as Redis
    participant F as Provider

    B->>Q: GET /api/dashboard
    Q->>PG: visible holdings for user (AsNoTracking → DTO)
    Q->>MD: GetLatest(tickers)
    MD->>R: newest entry per sorted set

    alt all fresh
        R-->>MD: observations
    else some missing or stale
        Note over MD: coalescer — 10 concurrent<br/>requests → 1 provider call per symbol
        MD->>F: fetch just the stale symbols
        F-->>MD: quotes
        MD->>R: ZADD (so the next request hits cache)
    end

    MD-->>Q: prices + per-ticker observedAt
    Note over Q: join in memory · decimal maths ·<br/>value, cost, profit, %, weight
    Q-->>B: DashboardDto — money as STRINGS
```

Read-through is why the poller is an **optimisation** rather than the only path to a price. It covers a blank dashboard outside market hours and a just-added ticker that the poller has not reached yet — and, now that the poll set is read live from Portfolio each cycle, there is no cached ticker table left to drift out of sync.

Money is serialised as strings. `System.Text.Json` writes `decimal` as a JSON number and `JSON.parse` makes it a double, so server-side `decimal` maths is destroyed at the boundary otherwise. Weight is computed server-side for the same reason.

---

## 4. Deployment topology

```mermaid
flowchart TB
    subgraph GH["GitHub"]
        Pages["GitHub Pages<br/>React SPA — static"]
        GA["Actions — OIDC, no stored secret"]
    end

    subgraph AZ["Azure — one resource group"]
        ACR["Container Registry — Basic"]
        subgraph ENV["Container Apps Environment — Consumption"]
            API["API container app<br/>minReplicas 1 · maxReplicas 2<br/>concurrentRequests 100"]
            JOB["Migrations Job<br/>Manual · parallelism 1"]
        end
        PG[("Postgres Flexible B1ms<br/>35 user connections")]
        RD[("Azure Managed Redis<br/>Balanced B0, HA off")]
    end

    Pages -->|"REST + SSE, cross-origin"| API
    GA -->|push image| ACR
    GA -->|deploy| ENV
    GA -->|publish| Pages
    ACR -.->|"managed identity pull"| API
    ACR -.-> JOB
    JOB -->|"as migrator role"| PG
    API -->|"4 roles × pool size 2"| PG
    API -->|"windows · claims · cooldowns · tickets · pub-sub"| RD

    style Pages fill:#2d4a3e,stroke:#4ade80,color:#e8f5e9
    style API fill:#1e3a5f,stroke:#60a5fa,color:#e3f2fd
```

Three consequences that shape the code, all designed for from Phase 1.

Cross-origin is permanent, so `ingress.corsPolicy` lists the Pages origin explicitly. The SSE ticket handshake is mandatory — `EventSource` cannot set headers, and cross-origin cookies are unreliable now that third-party cookies are being phased out, so `POST /api/alerts/stream-ticket` returns a single-use 30-second token consumed as a query param. And a 20-second heartbeat is not optional: ACA's `requestIdleTimeout` is 4 minutes, and 4 is both the default *and* the floor on Consumption, since raising it needs a Dedicated D4+ profile with two nodes that costs more than the rest of the stack.

`minReplicas: 1` is load-bearing — scale to zero and ingestion stops. `maxReplicas: 2` is what the Postgres connection budget allows.

Locally, `docker compose up` runs the same API plus an nginx frontend container, Postgres and Redis. The brief requires the whole stack in one command, so the frontend container stays even though production serves it from Pages.
