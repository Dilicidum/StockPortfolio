# Service interactions — who talks to what at runtime

The running system: the browser, the one process the modules live in, and the stores and services outside
it. [module-interactions.md](module-interactions.md) covers the same ground at the level of code
references and deployment; this file is the runtime picture only.

```mermaid
flowchart TB
    UI["Browser<br/>React single-page app"]

    subgraph HOST["The API — one process"]
        GATE["Token check<br/>verifies the token Identity signed;<br/>skipped for register, sign in and refresh"]
        ID[Identity]
        PF[Portfolio]
        MD[MarketData]
        AL[Alerts]
        POLL["The poll loop<br/>the only thing here<br/>that runs without a request"]
    end

    MIG["Migrator<br/>a separate job"]
    PG[("PostgreSQL")]
    RD[("Redis")]
    FH["The price provider"]

    UI -->|"register, sign in, refresh — no token yet, this is where one is issued"| ID
    UI -->|"every other request carries the token"| GATE
    GATE -->|"who am I, sign out"| ID
    GATE -->|"add holdings, load the dashboard — caller already verified"| PF
    GATE -->|"set a threshold, read history, take a stream ticket, simulate"| AL
    UI -->|"the live stream — no token, a single-use ticket instead"| AL
    PF -->|"a position needs today's price to be worth anything"| MD
    AL -->|"how has this moved over the window?"| MD
    AL -->|"does this user hold this ticker?"| PF
    POLL -->|"which tickers are worth sampling, and here is each sample"| AL
    POLL -->|"fetch and store, once a cycle"| MD
    ID -->|"users and sign-in sessions"| PG
    PF -->|"the positions themselves"| PG
    AL -->|"thresholds and what has fired"| PG
    MD -->|"the last price it saw, so the dashboard survives an outage;<br/>and a trimmed recent series per watched ticker"| RD
    AL -->|"cooldowns, stream tickets, and the fan-out channel"| RD
    MD -->|"fetches live quotes"| FH
    MIG -->|"creates the tables before the API is allowed to start"| PG

    style PF fill:#14532d,stroke:#4ade80,color:#dcfce7
    style MD fill:#78350f,stroke:#fbbf24,color:#fef3c7
    style AL fill:#1e3a5f,stroke:#60a5fa,color:#e3f2fd
    style POLL fill:#1e3a5f,stroke:#60a5fa,color:#e3f2fd
    style ID fill:#334155,stroke:#94a3b8,color:#e2e8f0
    style FH fill:#4c1d24,stroke:#f87171,color:#fee2e2
    style UI fill:#2d4a3e,stroke:#4ade80,color:#e8f5e9
```

The poll loop is drawn separately from the modules on purpose. It lives inside MarketData, but it is
reached by a timer rather than by a request, and it is the reason the deployed API can no longer be left
with no copy running.

The browser is served from GitHub Pages and the API runs in Azure, so the two are always on different
addresses. Everything between them is plain HTTP with the sign-in token attached.

## Why the token check sits outside Identity

Identity **issues** the token; the host **verifies** it. Splitting those two is deliberate, and it is the
thing that would make Identity cheap to pull out into its own service.

Verifying a token needs three things — the key, the issuer name and the audience name. All three are
settings. It needs no code from Identity and no call to it, which is the whole point of a self-contained
token. If verification lived inside Identity instead, then pulling Portfolio out into its own service
would leave it two bad options: carry a copy of Identity's code, or ask Identity over the network on every
single request. The second turns Identity into the thing that takes the whole system down when it stops
answering. Real systems avoid both by doing exactly what is here — the issuer signs, and every consumer
verifies locally.

**One thing does have to change on extraction.** The key today is a single shared secret, so anything that
can verify a token can also mint one. Splitting into separate services means moving to a key *pair*:
Identity keeps the half that signs, everyone else gets the half that only verifies. That is a settings and
algorithm change, not a move of code.

## What each module stores, and where

| Module | PostgreSQL | Redis | Outside |
|---|---|---|---|
| **Identity** | its own schema — users, sign-in sessions | — | — |
| **Portfolio** | its own schema — positions | — | — |
| **MarketData** | **nothing** | the last price seen for each ticker, each ticker's company name, a trimmed recent series for each watched ticker, and the two poll locks | the price provider, over HTTP |
| **Alerts** | its own schema — thresholds and fired alerts | cooldowns, stream tickets, and the channel that pushes an alert to the browser | — |

**MarketData has no database at all**, and that is deliberate: everything it keeps is one value per
ticker, which expires and can be re-fetched. Giving it a database would buy an empty migration and a row
of bookkeeping for no behaviour.

**Each module connects as its own database user**, with no permission to read another module's schema.
That is not a convention — a query across the line is refused by the database itself.

## The one loop that runs without a request

A background loop asks the provider for prices on a timer, but **only for tickers somebody has an alert
on**. It writes a short price history to Redis, compares each new price against it, and pushes a message
to the browser over a long-lived connection when a threshold is crossed. With nobody's alert configured
the loop still wakes, finds an empty list and calls nothing.

Three things about it are load-bearing at runtime and easy to lose:

- **Two locks, not one.** One picks which copy of the app polls a given cycle; the other stops a cycle
  that overran from being joined by the next one on a different copy. Both expire, as a backstop for a
  copy that dies mid-cycle.
- **The alert is written down before it is pushed.** Whether anyone is connected only decides whether it
  also arrives now. A failed push costs nothing, because the next history load finds the row.
- **The stream is authenticated by a ticket, not a header**, and every copy of the app subscribes to the
  channel so an alert produced on one copy still reaches a browser attached to another. Without that
  fan-out, alerts stop arriving for some users the moment there is more than one copy.
