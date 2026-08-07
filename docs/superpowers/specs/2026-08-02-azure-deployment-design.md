# Azure deployment — design

**Date:** 2026-08-02
**Status:** **deployed and verified.** It took six attempts; every failure is recorded under Outcome.

| | |
|---|---|
| API | `https://stockp-api-qdgz3wugqbihs.icysea-481b5825.polandcentral.azurecontainerapps.io` |
| SPA | `https://dilicidum.github.io/StockPortfolio/` |
| Resource group | `stockportfolio-rg`, `polandcentral`, `deleteAfter: 2026-08-20` (the 2026-08-06 deploy plus 14 days) |

The `deleteAfter` date decides when `teardown.yml` destroys the resource group. Read it from the
resource group's own tag before relying on it; every deploy moves it.

**Phase 3 shipped on 2026-08-05** (PR #2, run 31043996353), and the deploy needed no local Azure CLI
— the workflow installs Bicep and runs `what-if` itself. Every step was green, including migrations
and the readiness smoke test. Verified afterwards: `/health/ready` → 200, `/api/marketdata/health` →
`{"provider":"Finnhub"}`, `/api/dev/nudge` → **404** in Production (it is gated on the environment
*and* on `IQuoteNudge`, so it is absent rather than merely protected), a real register → add →
dashboard round trip returning live market prices, and a symbol that does not exist returning
`UnknownTicker`.

The goal was to deploy the StockPortfolio API to Azure Container Apps and the SPA to GitHub Pages,
driven by GitHub Actions, under a hard personal spending ceiling of **$100**.

This file sits in `docs/superpowers/specs/` rather than `docs/plan/` on purpose: `docs/plan/` is a
numbered phase structure describing the product build, and this is operational work that cuts
across it.

## The problem this answered

`infra/` and `.github/workflows/deploy.yml` were written in full during Phase 1 and had never been
run. `bicep build` had never run; nothing had ever been deployed. The infrastructure described the
*finished* system from `docs/plan/`, while `src/` held the Phase 1 system. Both were internally
consistent, so nothing errored — but the difference would be billed.

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
subscription per month. At the configured 0.5 vCPU that covers roughly 100 hours of runtime, so with
`minReplicas: 0` and demo traffic the compute line rounds to zero.

**Predicted: ~$33/month, ~$1.08/day. Measured while the API still scaled to zero: ~$1.26/day.** Both
figures belong to that configuration. `minReplicas` went back to 1 in Phase 4 (D1 below), so the
compute line no longer rounds to zero and the real rate is higher. Nobody has measured how much
higher — [DEPLOYING.md](../../DEPLOYING.md) says how to read it rather than quoting a number.

## Decisions

### D1 — `minReplicas`: 0 for Phases 1–3, back to 1 from Phase 4

Setting it to 0 saved the entire $0–34 Container App line; the fixed floor was unaffected. The cost
was a cold start on the first request after idle, accepted for a demo.

**It went back to `1` in Phase 4 and must stay there.** `main.bicep` passes 1, matching the default
in [containerapp-api.bicep](../../../infra/modules/containerapp-api.bicep). The quote poller is what
ended the saving: a sleeping replica samples no prices, so no alert ever fires and nothing reports a
fault. The poller and the always-on replica are one decision — reverting either alone leaves a feature
that silently stops working whenever traffic does. The condition to check is not the phase number but
whether `src/` still contains a `BackgroundService`, `IHostedService` or `PeriodicTimer`; it does.

The compute line is therefore no longer near zero. An always-on replica is billed at the active rate
around the clock and a held-open alert stream never qualifies for the reduced idle rate either.

The poller and the Redis price window are alert infrastructure and belong to **Phase 4**. The
dashboard fetches prices from the provider on demand, so it needs nothing running in the background.

The instruction is deliberately phrased as *"when MarketData ships its poller"* rather than *"in
Phase 4"*: the condition is what makes `0` unsafe, and it survives the phase numbering moving again.
The exit checkbox "no `BackgroundService`, `PeriodicTimer` or `IHostedService` anywhere in `src/`" is
that condition made checkable. It still holds, so `0` is right today.

### D2 — Redis stays

Explicit user decision. Redis earned nothing at the time it was decided, but $11.68 over a one-to-two
week deployment is not worth the code churn of making it conditional, and Phase 3 needs it anyway.

### D3 — the cost ceiling is enforced by time, not by money

An Azure budget **cannot** enforce a ceiling. Per Microsoft's budget quickstart: *"When the budget
thresholds you've created are exceeded, notifications are triggered. None of your resources are
affected and your consumption isn't stopped."*

An Azure **spending limit** is a real hard stop, but per the spending-limit doc it *"isn't available
for subscriptions with commitment plans or with pay-as-you-go pricing"*, and *"custom spending
limits aren't available"* — where it does exist it equals the credit amount and cannot be set to
$100.

So the guarantee comes from bounding how long the deployment lives, which we control exactly:

| Control | Mechanism | Effect |
|---|---|---|
| Burn rate | small resources, one small replica | read it, do not quote it — see `DEPLOYING.md` |
| Deadline recorded | deploy stamps resource-group tag `deleteAfter` = today + 14 days | visible in Azure itself |
| Enforcement | daily scheduled workflow deletes the group once `deleteAfter` has passed | maximum exposure ~$15 |
| Tripwire | budget alert at $25 and $50 | email only, explicitly not a cap |

At $32.79/month, reaching $100 takes 91 days. A 14-day teardown makes it unreachable by arithmetic.
Worst case, with every replica active for the full window, is about $31.

Re-deploying re-stamps `deleteAfter`, so the window extends by being used.

**Known gap:** GitHub disables scheduled workflows in repositories with 60 days of no activity. That
does not matter at a 14-day window, and the budget email is the backstop.

**What teardown does with a bad tag.** If the `deleteAfter` tag is missing or cannot be read, the
workflow deletes anyway. Both choices are unpleasant — deleting can destroy a deployment someone
meant to keep, and skipping means one typo silently turns off the only real cost guarantee. Deleting
is the safer of the two here, because a group with no readable deadline is a group nothing is
bounding.

### D4 — Postgres roles are created by the deploy workflow

`db/init/01-roles.sql` declares itself the single source of truth, executed by "Compose,
Testcontainers and the Azure provisioning job". That was true of the first two and false of the
third: `src/Migrator/Program.cs` only applies EF migrations, and `deploy.yml` had no step that ran
the SQL. On a fresh Flexible Server the migration job connects as `migrator`, which did not exist,
and the deploy failed before anything went live.

Compose gets roles from `docker-entrypoint-initdb.d`; Testcontainers uses the same hook. Azure
Flexible Server has no equivalent, so the one environment that needs an explicit step is the one
that cannot be rehearsed locally.

Fix: a step in `deploy.yml`, before the migration job, that

1. adds a temporary Postgres firewall rule for the runner's own public IP,
2. runs the existing `db/init/01-roles.sql` through `psql` as the Postgres admin, supplying the same
   variables `00-roles.sh` supplies,
3. removes the firewall rule, in a step that runs even on failure.

Running the existing file as-is, rather than reimplementing the role model in Bicep, is the point: it
makes the file's single-source-of-truth claim true instead of aspirational. `01-roles.sql` is already
safe to run more than once (`\gexec` over `NOT EXISTS`, `CREATE SCHEMA IF NOT EXISTS`), so running it
on every deploy is fine.

`GRANT migrator TO CURRENT_USER` at line 72 already anticipates this: the Azure admin is not a
superuser.

## What the user had to supply

Only the user could provide these, and all of them are now in place: an Azure subscription and tenant
ID, a resource group name and region, an Entra app registration with a federated credential, and
Owner or User Access Administrator rights on the group (`main.bicep` creates an AcrPull role
assignment). Six Postgres passwords and the JWT signing key (≥32 bytes) were generated locally and
placed as GitHub secrets. `FINNHUB_API_KEY` is optional; empty is a supported path, and the app then
falls back to `FakeQuoteProvider`.

Azure CLI is still not installed on the development machine. That blocks a local `bicep build` and a
local `what-if` rehearsal. It does **not** block a deploy — the workflow installs Bicep and runs
`what-if` in the runner.

Subscription type is worth confirming first (portal → Cost Management + Billing → subscription
overview). A "Remove spending limit" banner means credit-based, and a hard stop already exists. No
banner means pay-as-you-go, and D3 is the entire guarantee.

## Out of scope

- Making Redis conditional (D2)
- Any change to application code beyond what D4 requires
- Custom domains, TLS certificates, staging slots
- Putting `minReplicas` back to 1 — tracked in D1, done when the poller lands, in Phase 4

## Verification

The deploy is not "done" when the workflow is green. It is done when:

1. `bicep build infra/main.bicep` succeeds locally
2. `az deployment group what-if` runs clean against the real resource group
3. `/health/ready` returns 200 over the public FQDN
4. Register → login → refresh → logout works in a browser against the deployed API
5. The resource group carries a `deleteAfter` tag with the expected date
6. The teardown workflow has been proven against a throwaway resource group — a teardown that has
   never run is not a guarantee, and this is the one control the $100 ceiling rests on

All six passed. Step 3 is the one that proves the most: `/health/ready` returning 200 means Postgres
*and* Redis are reachable, so D4 worked. Beyond the listed checks, `POST /api/auth/register` returned
201 with a token pair and `GET /api/auth/me` returned the user — which exercises the migrated schema,
the service-role grants, Argon2 hashing and JWT signing in a way no health check does.

## Outcome

Six deploy attempts. None of this was predictable from reading the code; all of it came from first
contact with a real subscription.

| # | Failure | Cause |
|---|---|---|
| 1–2 | `AADSTS700213` at login | GitHub issues **immutable OIDC subject claims** carrying numeric owner and repo IDs. The documented `repo:<owner>/<repo>:…` form matches nothing. |
| 3 | `MissingSubscriptionRegistration` | A new pay-as-you-go subscription registers almost no resource providers. Six needed registering by hand. `az provider show` returns region lists regardless, so the availability check passed while the deploy could not. |
| 4 | `AppLogsConfiguration.Destination is invalid` | `destination: 'none'` as a literal string is rejected by an error message that lists `none` as valid — it means the property *omitted*. |
| 5 | `ParentResourceNotFound` on `redisEnterprise/databases` | `existing` + `listKeys()` creates no dependency on the module that builds the resource. It would have "passed" on retry and shipped as a guaranteed first-deploy failure. |
| 6 | `--server-name` / `--name` | Two independently wrong CLI flags on `firewall-rule create`, surfacing one per deploy cycle. |

The teardown workflow had its own bug, caught during testing and worth more than the deploy fixes: it
treated *any* `az group show` failure as "the group is absent" and exited green, so a revoked role
assignment or an expired credential would have produced a passing run with nothing torn down. The
first two teardown tests reported success without ever reaching the decision. It now inspects the
error and fails on anything that is not a clean not-found.

Four of these are recorded in `CLAUDE.md` as traps.
