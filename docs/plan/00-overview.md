# StockPortfolio — Overview

Six vertical phases, 6.0 days. Each phase ships screens + backend + tests + a deploy.

| # | Phase | What you can do at the end | Days | File |
|---|---|---|---|---|
| 1 | Sign in | Register and log in on a public Azure URL and locally | 1.25 | [phase-1-sign-in.md](phase-1-sign-in.md) |
| 2 | My portfolio | Add, merge, edit and delete holdings | 0.75 | [phase-2-my-portfolio.md](phase-2-my-portfolio.md) |
| 3 | Live prices & P&L | Dashboard with real prices, totals, profit/loss | 1.1 | [phase-3-live-prices.md](phase-3-live-prices.md) |
| 4 | Alerts | Threshold alerts pushed live over SSE | 0.9 | [phase-4-alerts.md](phase-4-alerts.md) |
| 5 | Make it mine | Theme, language, interval, threshold, visibility, BYOK | 0.6 | [phase-5-make-it-mine.md](phase-5-make-it-mine.md) |
| 6 | Doesn't break | Visible degradation when dependencies fail | 0.75 | [phase-6-doesnt-break.md](phase-6-doesnt-break.md) |

**5.4 days against a 6-day clock**, so there is about half a day of slack. Phases 1–3 cover every P0 requirement, so the acceptance gate is passed by end of day 3.

Reference diagrams: [module-boundaries.md](module-boundaries.md) — the criterion behind the four modules, what is in each, the three places a boundary was deliberately *not* drawn, and who owns which byte across Postgres, Redis and the browser. [er-diagram.md](er-diagram.md) — the schemas, what lives in Redis instead, and the indexes that carry weight. [module-interactions.md](module-interactions.md) — module dependency graph, what crosses each boundary, runtime sequences for the poll cycle and the dashboard, and the deployment topology.

Phase 4 still ships alerts as a phase; it no longer ships them as a *module*. See "Three modules, not four" below.

---

## Context

`TZ_Stock_Portfolio_App.docx` is a 7-day take-home: a stock-portfolio tracker with live quotes, P&L, and real-time threshold alerts. `docs/Initial.md` is the architecture — modular monolith, Postgres schema + role isolation, Redis price windows, SSE alert delivery. It says **four** modules; Phase 2 cut that to three, and the argument is in "Three modules, not four" below. `Initial.md` stays historical and is not edited for it.

A validation team audited that architecture against the brief. It holds up technically; the gaps were about **graded surface**. Three P0 requirements had no design at all (TanStack Router routing, compose-with-frontend, client session handling), req 8's dashboard settings were missing entirely while `Initial.md:66` and `:150` hard-code the 60-second cadence it asks you to make configurable, and there were two internal contradictions plus a false positive in the alert rule. All are addressed in the phases.

The task-giver said *«Используй все что посчитаешь нужным»*, which settles the tech-choice latitude — including SSE over WebSockets.

## Decisions already made

| Decision | Why / consequence |
|---|---|
| **SSE**, not WebSockets | README carries the decision matrix; the mockup's "WS Live" badge is renamed. |
| **OneOf** for result unions | Over a hand-rolled closed hierarchy. |
| **Multi-replica claim kept** | Container Apps makes the topology real. |
| **Frontend → GitHub Pages**, API → Container Apps | Drops a container from Bicep and the bill. |
| **Azure Managed Redis Balanced B0**, HA off | ≈$13.14/mo. *Not* Azure Cache for Redis, which is retiring. |
| **Read-through on cache miss** | A dashboard request fetches its own prices if the window is empty — so the poller is an optimisation, not the only path. |
| **UI: match the mockup, minus polish** | Same screens, controls, layout. No hero, no ticker strip, no ornament. |
| **No alert replay** | Req 9 asks for an event on breach, a background check, and a simulate button. Persistence, offline delivery and cursor replay are `Initial.md:134-136`, not the requirement. Alerts are written to Postgres and the panel loads history with a plain `GET`. |
| **No watchlist** | «Перелік акцій» in req 8 sits inside *dashboard settings*, so it means which of your holdings show on the dashboard — an `is_visible` flag, not a second list of stocks you don't own. |
| **No cached ticker table** | `Initial.md:74` gives MarketData its own table of distinct tickers kept in step by events. The held-ticker list is read live from Portfolio each cycle instead, which removes the table, both handlers, a reconciliation pass and a divergence failure mode. |
| **Alerts is not a module** | Reversed during Phase 2. Alerts is a feature area inside Portfolio; the module count is three. Reasoning below. |

---

## Three modules, not four

The plan shipped with four modules — `Identity`, `Portfolio`, `MarketData`, `Alerts` — and Phase 2 reduced that to three by folding Alerts into Portfolio. Recorded here in full because several files in `docs/plan/` argue for the four-way split, and a reversal that does not name what it reverses leaves the next reader following the older argument.

**Why the split was wrong.** A bounded context is delimited by *ubiquitous language*: the same word meaning genuinely different things on either side of the line. `Ticker` means a symbol in Portfolio, a symbol in MarketData and a symbol in Alerts — no divergence anywhere. So it was one context split three ways, not three contexts. `phase-2-implementation.md` §2.2 defends declaring `Ticker` three times as *"not duplication… it is what lets three modules be pulled apart"*; by DDD's own test, it was duplication.

**What actually applies is subdomain classification.**

| Subdomain | Module | Why |
|---|---|---|
| **Core** | Portfolio, alerts included | The differentiator — holdings, P&L and the threshold behaviour built on top of them |
| **Generic** | Identity | Register/login/refresh. In production you would buy this, not write it |
| **Supporting** | MarketData | Necessary, not differentiating, and with its own lifecycle: timer-driven, external API, a distinct failure mode |

**The code was already saying so.** `HoldingRemoved` was the only domain event in the entire six-phase plan, and it existed for exactly one reason: Alerts could not call into Portfolio, so removal had to be announced. Inside one module it is a method call.

**Consequence: the domain-event infrastructure is deleted.** `Shared.Kernel/DomainEvents/` — `IDomainEvent`, `IDomainEventHandler`, `IDomainEventPublisher` — is gone, along with Phase 2's Task 1, its Task 10 (dispatch interceptor, publisher and six tests) and the `HoldingRemoved` part of Task 5. Phase 1 had already written `IDomainEvent`, found nothing raised it and deleted it; bringing it back with still no raiser would have repeated that exactly.

**Dependency direction is now Portfolio (alerts included) → MarketData**, with Identity off to the side and zero inbound runtime coupling.

**What did not change.** Every alert requirement in Phase 4 still ships: thresholds, windows, cooldowns, evaluation after each poll, fired-alert history, the simulate endpoint, the SSE stream and the ticket handshake. The `/api/alerts/*` URL space is unchanged. They are built in `Portfolio.Domain` / `.Application` / `.Api` under an `Alerts/` feature area instead of in five projects of their own.

---

## Deployment topology

| Target | What runs there | Why |
|---|---|---|
| **`docker compose up`** | Frontend (nginx) + API + Postgres + Redis | **P0 req 7** — the brief demands the whole stack locally, one command. The frontend container stays. |
| **GitHub Pages** | React SPA, static | Free, no container. Built by Actions with `VITE_API_BASE_URL` baked in. |
| **Azure Container Apps** | API only | One app, `minReplicas: 1` (or ingestion stops), `maxReplicas: 2`. |

Three consequences designed for from Phase 1:

- **Cross-origin is permanent.** ACA `ingress.corsPolicy` lists the Pages origin explicitly.
- **The SSE ticket handshake is mandatory.** `EventSource` cannot set headers and cross-origin cookies are unreliable now that third-party cookies are being phased out. `POST /api/alerts/stream-ticket` (bearer) → single-use token, 30s TTL → `EventSource('…/stream?ticket=…')`.
- **GitHub Pages needs `404.html`** (a copy of `index.html`) for SPA history routing, plus Vite `base: '/<repo>/'` and a matching router `basepath`.

---

## Stack — versions verified 2026-08-01

**Backend** — .NET 10 SDK 10.0.302, C# 14, xUnit v3

`Microsoft.EntityFrameworkCore` **≥10.0.7** · `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 · `OneOf` 3.0.271 + `OneOf.SourceGenerator` · `Microsoft.Extensions.Http.Resilience` · `Konscious.Security.Cryptography.Argon2` 1.3.1 · `StackExchange.Redis` 3.1.0 · FluentValidation · Testcontainers · `Microsoft.Extensions.TimeProvider.Testing`

> ⚠️ **Correction (2026-08-02).** An earlier revision of this list named `OneOfDiagnosticSuppressor`. **That package does not exist on nuget.org**, and it is not needed — `.Match` is exhaustive by arity. See `phase-1-implementation.md` §3. Exact resolved versions now live in `Directory.Packages.props`, which is the single source of truth; this list is orientation only.

**Frontend** — Node 24

React **19.2.8** · Vite **8.2.0** · `@tanstack/react-router` **1.170.18** (v1 *is* current — there is no v2) · `@tanstack/react-query` **5.101.4** · Tailwind **4.3.3** + `@tailwindcss/vite` · react-hook-form 7.84 + zod 4.4 + `@hookform/resolvers` 5.6 · i18next 26.3 + react-i18next 17.0 · Vitest 4.1 + RTL 16.3 + MSW 2.15 · TypeScript pinned (`latest` is now 7.0.2, the Go port)

**Zero external UI component libraries.** Radix, Headless UI and React Aria all ship markup and self-describe as component libraries; the brief bans UI kits and ends its list with "тощо". No screen here needs a focus trap anyway — native `<select>`, `<input role="switch">`, route-based tabs.

---

## Solution layout

```
src/
  Shared.Kernel/                 Money, CQRS interfaces, InvalidInput — framework-free
  Shared.Api/           ValidationFilter<T>, ProblemDetails helpers
  Modules/
    Identity/    .Contracts · .Domain · .Application · .Infrastructure · .Api
    Portfolio/   same five — holdings and alerts
    MarketData/  same five
  Api/                           host: DI, endpoint registration, middleware
  Migrator/                      console; applies every DbContext as the `migrator` role
  Web/                           React SPA
tests/
  <Module>.UnitTests             per module, no infrastructure
  Api.IntegrationTests           Testcontainers Postgres + Redis
  Architecture.Tests             module boundary rules
infra/                           Bicep
.github/workflows/               CI + deploy
```

---

## Non-negotiables

Assumed by every phase file.

**Architecture** — accessibility follows the onion, **not** a blanket `internal`: `.Domain`, `.Application` and `.Api` are `public`, `.Infrastructure` is `internal` except one `<Module>Module` seam. (`internal` is per-assembly and a module is five assemblies, so blanket-`internal` cannot compile.) A module references only other modules' `.Contracts`, enforced by `Architecture.Tests` rather than the compiler. `.Contracts` holds records of primitives only — no EF reference, no aggregates, no strongly-typed IDs, raw `Guid`. See `phase-1-implementation.md` §4.2.

**Three Postgres schemas + three roles, `Maximum Pool Size=2`.** Azure Postgres B1ms allows **35 user connections** (50 total, 15 reserved), and a different `Username` is a different Npgsql pool. Npgsql's default pool size of 100 × 3 roles × 2 replicas would request 600. PgBouncer is unavailable on Burstable, so there is no escape hatch below this. The database still creates a fourth schema and role, `alerts` / `alerts_svc`, which nothing connects as — see Open items.

**CQRS** — `ICommandHandler<,>` / `IQueryHandler<,>` injected directly into Minimal API endpoints. **No dispatcher**: one caller per handler, in the same module, so there is nothing to decouple — and the concrete type gives better OpenAPI metadata. Cross-cutting concerns are DI decorators, which work without a mediator. Add a dispatcher only if a second caller appears.

**Results** — a handler returns `OneOf<…>` of its outcomes **directly**, mapped to `TypedResults` via `.Match`. No `[GenerateOneOf]` and no named union class: the wrapper hides the outcome list behind a name for no gain. `<UseCase>Result` means the *success payload*, and each failure record lives beside the use case that returns it; `InvalidInput` in `Shared.Kernel` is the one shared failure. Exhaustiveness comes from `.Match`'s arity, not from an analyzer: add a case and every call site breaks. Never `switch` over `.Value`, never silence CS8509 with `_ => throw`, and name every `.Match` lambda parameter.

**Rich domain, and no base class** — there is no `AggregateRoot<TId>` and no `IDomainEvent`; both were written, found to carry nothing, and deleted. Phase 2 planned to reintroduce the event type for `HoldingRemoved` and then did not: the Alerts merge removed the only consumer, so there is again no raiser. Each entity declares its own `Id` and has **exactly one constructor**: private, taking every mapped value, assigning and nothing else. No parameterless constructor, no object initialiser, no public setter — so a half-built entity is not representable and the static `Create(…)` returning a OneOf is the only way in. Instance methods enforce invariants and **throw**. Three EF rules that bite otherwise:

- `PropertyAccessMode.PreferField` is the default since EF Core 3.0, so **EF never calls your setter**. Validation placed in a setter silently never runs.
- A constructor whose **parameter names match mapped property names is selected for materialisation**. Binding is by convention and accessibility-blind, and cannot be configured. That is fine and intended here — the constructor only assigns — and becomes a trap the moment a guard is added inside it, because EF re-runs that guard on every row of every `SELECT`. Guards belong in the factory, which EF never calls. Renaming a parameter without renaming its property leaves no bindable constructor and the **whole model fails to build at startup**.
- **`HasDefaultSchema` does not move `__EFMigrationsHistory`** (efcore#24127, closed *not planned*). Without `MigrationsHistoryTable(name, schema)` per context, all three contexts share one history table and corrupt each other's bookkeeping in ways that look like data corruption.

**Validation placement** — shape → the request record in `.Api` (FluentValidation, run by a generic `IEndpointFilter`, **not** a DI decorator); context (exists? allowed?) → handler, as a result case; invariant → entity, and entity guards **throw** rather than returning result cases. Requests live in `.Api/Requests/` and the endpoint builds the command with `new`; an `.Application` type never binds off the wire. Do not use the built-in Minimal API `AddValidation()`: it is DataAnnotations-attribute-driven and awkward for conditional or cross-field rules.

**Testing** — xUnit v3. Unit tests touch no infrastructure. Integration tests share one Testcontainers collection fixture across the assembly and need `public partial class Program { }` in the API. `FakeTimeProvider` for anything timer-driven. Architecture tests assert module boundaries by reflection over assembly references — no NetArchTest.

---

## Definition of Done — identical for all six phases

1. Backend + UI working against each other
2. Unit tests green
3. Integration tests green (Testcontainers)
4. Screens responsive — container queries, table → cards on mobile
5. `docker compose up` from a clean clone still works
6. Deployed and verified on the public URL
7. README section added

A phase is not done because tests pass. It is done when the thing works in a browser.

---

## Cost

| Line | Monthly |
|---|---|
| Container Apps — 1 API app, 0.25 vCPU / 0.5 GiB, always-on, after free grant | ≈ $18 |
| Postgres Flexible B1ms + 32 GB | ≈ $16 |
| Azure Managed Redis Balanced B0, HA off | ≈ $13 |
| ACR Basic | ≈ $5 |
| GitHub Pages | $0 |
| Log Analytics (`destination: 'none'`) | $0 |
| **Total** | **≈ $52/month**, ≈ $12 for a one-week demo |

The ACA free grant is 180,000 vCPU-seconds/month ≈ 8.3 days of one always-on 0.25-vCPU replica. Verify AMR B0 in your region via the Pricing Calculator before the Phase 1 deploy — the official pricing page renders prices client-side. HA on roughly doubles the Redis line; keep it off. Teardown is `az group delete`; `az postgres flexible-server stop` halts compute billing immediately.

---

## Verification — full run before submission

From a clean clone with **no API key configured**. The fake provider must carry the demo: Finnhub shut down its sandbox in September 2022, so a grader would otherwise have to register.

```bash
git clone <repo> && cd <repo> && docker compose up
```

1. Register → dashboard → hard-refresh → still signed in
2. Add AAPL 10 @ $100, then 10 @ $150 → one row, 20 @ $125
3. Price appears within a cycle — and **immediately** for a newly added ticker, via read-through
4. Threshold to 1%, click **Simulate** → alert in under a second
5. Reload → the alert is still listed (history fetch, not replay)
6. Ukrainian + dark mode → both persist across reload
7. Hide a position → its row goes, it is still polled, it still alerts
8. Refresh interval to 15s → dashboard visibly refetches faster
9. Kill the provider → stale banner, health amber, no crash
10. 375px wide → table becomes cards
11. `docker compose down && up` → data persists

Then steps 1–5 against the GitHub Pages URL talking to the deployed API, watching specifically that the SSE stream survives past four minutes. ACA's `requestIdleTimeout` is **4 minutes and cannot be raised** on Consumption — raising it needs a Dedicated D4+ profile with two nodes. The 20-second heartbeat is what keeps it alive.

---

## Open items

- ~~Spike `OneOfDiagnosticSuppressor` against Roslyn 5~~ — **done 2026-08-02.** The package does not exist; it is also unnecessary. See `phase-1-implementation.md` §3.
- **`docs/Initial.md` needs three corrections** — alert-settings ownership (Phase 4), the window-claim overlap guard (Phase 3), the alert example's arithmetic (Phase 4). It also describes four modules, which is now wrong; it stays historical and is not edited for that.
- **The `alerts` schema, the `alerts_svc` role and the Alerts deployment variables still exist.** `db/init/00-roles.sh`, `docker-compose.yml`, `infra/*.bicep` and `.github/workflows/*` all still carry `ALERTS_PW` and an Alerts connection string. They were left in place deliberately when Alerts was merged into Portfolio: `docker compose up` is the P0 acceptance gate and there was no Docker daemon available to re-verify a clean-clone boot after removing them. Tracked with its trigger in [../deferred-work.md](../deferred-work.md).
- **Recompute the 50-ticker ceiling** before the README quotes it. Finnhub is confirmed at 60 calls/min with a 30/sec burst cap and no batch endpoint, but `Initial.md:184` admits the figure was never verified.
- **Confirm AMR B0 pricing in your region** before the Phase 1 deploy.
