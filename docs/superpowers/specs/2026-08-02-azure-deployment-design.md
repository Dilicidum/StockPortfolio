# Azure deployment — design

**Date:** 2026-08-02
**Status:** **deployed and verified.** Six attempts; every failure is recorded under Outcome.

| | |
|---|---|
| API | `https://stockp-api-qdgz3wugqbihs.icysea-481b5825.polandcentral.azurecontainerapps.io` |
| SPA | `https://dilicidum.github.io/StockPortfolio/` |
| Resource group | `stockportfolio-rg`, `polandcentral`, `deleteAfter: 2026-08-16` |

Deploy the StockPortfolio API to Azure Container Apps and the SPA to GitHub Pages, driven by
GitHub Actions, under a hard personal spending ceiling of **$100**.

This file sits in `docs/superpowers/specs/` rather than `docs/plan/` on purpose: `docs/plan/` is a
numbered phase structure describing the product build, and this is operational work that cuts
across it.

## Context

`infra/` and `.github/workflows/deploy.yml` were written in full during Phase 1 but have never been
executed. `bicep build` has never run; nothing has ever been deployed. The infrastructure describes
the *finished* system from `docs/plan/`, while `src/` contains the Phase-1 system — both internally
consistent, so nothing errors, but the difference is billable.

The subscription is personal and paid. Cost is a design constraint, not a footnote.

## Cost model

Priced from the Azure retail prices API, East US, consumption rates, 730 h/month.

| Line | Rate | Monthly |
|---|---|---|
| Postgres Flexible B1ms compute | $0.017/hr | $12.41 |
| Postgres storage, 32 GB | ~$0.115/GB/mo (not separately verified) | ~$3.70 |
| Azure Managed Redis Balanced B0 | $0.016/hr | $11.68 |
| ACR Basic | $0.1666/day | $5.00 |
| **Fixed floor** | | **$32.79** |
| Container App, 0.5 vCPU + 1 GiB | idle $0.000003/s · active vCPU $0.000024/s | $0 – $34.00 |

The Container Apps Consumption free grant is 180,000 vCPU-seconds + 360,000 GiB-seconds per
subscription per month. At the configured 0.5 vCPU that covers ~100 hours of runtime, so with
`minReplicas: 0` and demo traffic the compute line rounds to zero.

**Target: ~$33/month, ~$1.08/day.**

## Decisions

### D1 — `minReplicas: 0`

Changed from `1`. Saves the entire $0–34 Container App line; the fixed floor is unaffected.

[containerapp-api.bicep:263](../../../infra/modules/containerapp-api.bicep) currently claims
`minReplicas: 1` is load-bearing because scale-to-zero stops the background quote poller. That
poller does not exist yet — `grep` for `BackgroundService`, `IHostedService` and `PeriodicTimer`
across `src/` returns zero hits. The comment describes Phase 3.

Cost: a cold start on the first request after idle. Accepted for a demo.

**This must be reverted to `1` when MarketData ships its poller.** The parameter stays in the Bicep
with its existing default of `1`; only the call site in `main.bicep` passes `0`, so the reversal is
a one-line change and the original rationale stays where it is.

### D2 — Redis stays

Explicit user decision. Redis is currently referenced only by `RedisHealthCheck`, so it earns
nothing today, but $11.68 over a one-to-two week deployment is not worth the code churn of making
it conditional, and Phase 3 needs it anyway.

### D3 — Cost ceiling is enforced by time, not by money

An Azure budget **cannot** enforce a ceiling. Per Microsoft's budget quickstart: *"When the budget
thresholds you've created are exceeded, notifications are triggered. None of your resources are
affected and your consumption isn't stopped."*

An Azure **spending limit** is a real hard stop, but per the spending-limit doc it *"isn't available
for subscriptions with commitment plans or with pay-as-you-go pricing"*, and *"custom spending
limits aren't available"* — where it does exist it equals the credit amount and cannot be set to
$100.

So the guarantee comes from bounding duration, which we control exactly:

| Guard | Mechanism | Effect |
|---|---|---|
| Burn rate | `minReplicas: 0` | ~$1.08/day |
| Deadline recorded | deploy stamps resource-group tag `deleteAfter` = today + 14 days | visible in Azure itself |
| Enforcement | daily scheduled workflow deletes the RG once `deleteAfter` has passed | max exposure ~$15 |
| Tripwire | budget alert at $25 and $50 | email only, explicitly not a cap |

At $32.79/month, reaching $100 takes 91 days. A 14-day teardown makes it unreachable by arithmetic.
Worst case with every replica active for the full window is ~$31.

Re-deploying re-stamps `deleteAfter`, so the window extends by using it.

**Known gap:** GitHub disables scheduled workflows in repositories with 60 days of no activity.
Irrelevant at a 14-day window; the budget email is the backstop.

**Open decision — teardown fail behaviour.** What the daily workflow does when the `deleteAfter` tag
is missing or unparseable is a genuine safety trade-off (delete anyway = fail-safe for cost but can
destroy a deployment someone meant to keep; skip = fail-open, and a tag typo silently disables the
only guarantee). To be decided at implementation time, not assumed.

### D4 — Postgres roles are bootstrapped by the deploy workflow

`db/init/01-roles.sql` declares itself the single source of truth executed by "Compose,
Testcontainers and the Azure provisioning job". That is true of the first two and false of the
third: `src/Migrator/Program.cs` only applies EF migrations, and `deploy.yml` has no step that runs
the SQL. On a fresh Flexible Server the migration job connects as `migrator`, which does not exist,
and the deploy fails before anything goes live.

Compose gets roles from `docker-entrypoint-initdb.d`; Testcontainers uses the same hook. Azure
Flexible Server has no equivalent, so the one environment that needs an explicit step is the one
that cannot be rehearsed locally.

Fix: a step in `deploy.yml`, before the migration job, that
1. adds a temporary Postgres firewall rule for the runner's own public IP,
2. runs the existing `db/init/01-roles.sql` via `psql` as the Postgres admin, supplying the same
   variables `00-roles.sh` supplies,
3. removes the firewall rule, in a step that runs even on failure.

Running the existing file verbatim rather than reimplementing the role model in Bicep is the point:
it makes the file's single-source-of-truth claim true instead of aspirational. `01-roles.sql` is
already guarded for re-execution (`\gexec` over `NOT EXISTS`, `CREATE SCHEMA IF NOT EXISTS`), so
running it on every deploy is safe.

`GRANT migrator TO CURRENT_USER` at line 72 already anticipates this: the Azure admin is not a
superuser.

## Prerequisites

Blocking, and only the user can supply them:

| # | Item | Current state |
|---|---|---|
| 0 | Branch named `main` | **the only local branch is `master`** — see below |
| 1 | GitHub repository | **`git remote -v` is empty** — no remote exists |
| 2 | Azure subscription ID and tenant ID | not supplied |
| 3 | Resource group name and region | not chosen |
| 4 | Entra app registration + federated credential | requires rights to register an app in the tenant |
| 5 | Owner or User Access Administrator on the RG | `main.bicep` creates an AcrPull role assignment |
| 6 | Azure CLI | **not installed on this machine** — blocks `bicep build` and `what-if` |

### Branch name mismatch

`deploy.yml` triggers on `push` to `main`; `ci.yml` triggers on `pull_request` to `main`. The only
branch in this repository is `master`. Pushed as-is, **neither workflow ever fires** — no error, no
warning, just silence, which reads identically to "nothing to do".

Rename `master` to `main` before adding the remote. Renaming matches GitHub's default and both
existing workflows; editing the workflows to say `master` would leave the repo disagreeing with its
own `CLAUDE.md`, which already records `main` as the main branch.

Generated locally, placed by the user: six Postgres passwords and the JWT signing key (≥32 bytes).

Optional: `FINNHUB_API_KEY`. Empty is a supported path — the app falls back to `FakeQuoteProvider`.

Subscription type should be confirmed first (portal → Cost Management + Billing → subscription
overview). A "Remove spending limit" banner means credit-based and a hard stop already exists; no
banner means pay-as-you-go and D3 is the entire guarantee.

## Out of scope

- Making Redis conditional (D2)
- Any change to application code beyond what D4 requires
- Custom domains, TLS certificates, staging slots
- Reverting `minReplicas` for Phase 3 — tracked in D1, done when the poller lands

## Verification

The deploy is not "done" when the workflow is green. It is done when:

1. `bicep build infra/main.bicep` succeeds locally
2. `az deployment group what-if` runs clean against the real resource group
3. `/health/ready` returns 200 over the public FQDN
4. Register → login → refresh → logout works in a browser against the deployed API
5. The resource group carries a `deleteAfter` tag with the expected date
6. The teardown workflow has been proven against a throwaway resource group — a teardown that has
   never run is not a guarantee, and this is the one control the $100 ceiling rests on

All six passed. `/health/ready` returning 200 is the load-bearing one: it proves Postgres *and*
Redis are reachable, so D4 worked. Beyond the listed checks, `POST /api/auth/register` returned 201
with a token pair and `GET /api/auth/me` returned the user — which exercises the migrated schema,
the service-role grants, Argon2 hashing and JWT signing in a way no health check does.

## Outcome

Six deploy attempts. Nothing here was predictable from reading the code; all of it came from a
first contact with a real subscription.

| # | Failure | Cause |
|---|---|---|
| 1–2 | `AADSTS700213` at login | GitHub issues **immutable OIDC subject claims** carrying numeric owner/repo IDs. The documented `repo:<owner>/<repo>:…` form matches nothing. |
| 3 | `MissingSubscriptionRegistration` | A new pay-as-you-go subscription registers almost no resource providers. Six needed registering by hand. `az provider show` returns region lists regardless, so the availability check passed while the deploy could not. |
| 4 | `AppLogsConfiguration.Destination is invalid` | `destination: 'none'` as a literal string is rejected by an error message that lists `none` as valid — it means the property *omitted*. |
| 5 | `ParentResourceNotFound` on `redisEnterprise/databases` | `existing` + `listKeys()` creates no dependency on the module that builds the resource. Would have "passed" on retry and shipped as a guaranteed first-deploy failure. |
| 6 | `--server-name` / `--name` | Two independently wrong CLI flags on `firewall-rule create`, surfacing one per deploy cycle. |

The teardown workflow had its own bug, caught during step 11 and worth more than the deploy fixes:
it treated *any* `az group show` failure as "the group is absent" and exited green, so a revoked
role assignment or expired credential would have produced a passing run with nothing torn down.
The first two teardown tests reported success without ever reaching the decision. Fixed to inspect
the error and fail on anything that is not a clean not-found.

Four traps from this exercise are recorded in `CLAUDE.md`.
