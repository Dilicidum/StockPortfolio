# Deploying

Operational instructions. Design rationale is in
[superpowers/specs/2026-08-02-azure-deployment-design.md](superpowers/specs/2026-08-02-azure-deployment-design.md).

## TL;DR

Everything is already provisioned and wired. **To deploy: push to `main`.** That is the whole
procedure. Do not run `az deployment group create` by hand.

```bash
git push origin main                                    # deploys
gh workflow run deploy.yml --repo Dilicidum/StockPortfolio   # or trigger manually
```

Add `[skip ci]` to the commit message when a push should **not** deploy.

## What exists

| | |
|---|---|
| Repo | `Dilicidum/StockPortfolio` — **public**, so Actions logs are world-readable |
| Resource group | `stockportfolio-rg` in `polandcentral` |
| API | `https://stockp-api-qdgz3wugqbihs.icysea-481b5825.polandcentral.azurecontainerapps.io` |
| SPA | `https://dilicidum.github.io/StockPortfolio/` |
| Burn rate | **Read it, do not quote it** — see below |
| Running | **Phase 4, deployed and verified 2026-08-06** (PR #6, run 31087381000) |
| Deletes itself on | **`deleteAfter`** — expected 2026-08-21 after that deploy, but **read the group's tag**; this line is a note, not the value |

`/api/marketdata/health` returns `{"provider":"Finnhub"}` on the live API, so the deployed dashboard
serves genuine prices rather than the fake. **Any deploy re-stamps `deleteAfter` to that day + 14**,
so the date moves without anyone editing this file. Read it from the resource group's tag, not from
the table.

Subscription, tenant and client IDs are in GitHub secrets, deliberately not written here — this
file is in a public repo.

## What the pipeline does

`.github/workflows/deploy.yml`, in order: bootstrap ACR → build/push both images → write the ARM
parameter file → what-if → deploy infra → **create Postgres roles** → run migrations and wait →
release the image → smoke-test `/health/ready` → build and publish the SPA to Pages.

Nothing goes live until migrations succeed.

## Verifying

```bash
gh run list --repo Dilicidum/StockPortfolio --workflow deploy.yml --limit 5

# Check the four database components BY NAME. Do not check the overall status.
curl -s <API_URL>/health/ready \
  | jq '[.components[] | select(.name | startswith("postgres-")) | {name, status}]'
# expect four rows, every status "Healthy"

curl -s <API_URL>/health/ready | jq '.components[] | select(.name == "redis") | {name, status}'
# informational only — Degraded here is not a failure

curl -s -o /dev/null -w '%{http_code}\n' <API_URL>/health/startup   # expect 200 — migrations applied
```

**Do not expect the overall `status` to read `Healthy`, and never gate on it.** The cache is
registered with a *degraded* failure status so that a Redis outage keeps the replica in rotation
instead of withdrawing it, and the framework maps Degraded to 200 — so the body legitimately reads
`"status": "Degraded"` while all four databases are fine. Demanding `Healthy` re-imposes the exact
failure that registration removed. The deploy's own smoke step works this way for the same reason: it
asserts `postgres-identity`, `postgres-portfolio`, `postgres-marketdata` and `postgres-alerts` each
report `Healthy` by name, prints `redis`, and asserts nothing about the overall status.

A green run is not proof. For a real check, `POST /api/auth/register` then `GET /api/auth/me` with the
bearer.

## What it costs

**No current figure is written down here, on purpose.** The only measurement anyone has taken is
roughly **$1.26 a day, and it was taken while the API still scaled to zero**. That configuration is
gone: `minReplicas: 1` bills a replica at the active rate around the clock, and a held-open alert
stream never qualifies for the platform's reduced idle rate either. So the real rate is higher, nobody
has measured how much higher, and inventing a number is worse than sending you to the meter.

Read the real one, per resource group, either way:

- **Portal** — Cost Management + Billing → Cost analysis → scope `stockportfolio-rg` → *Daily costs*.
  This is the authority; the CLI reads the same data.
- **CLI** — needs the `costmanagement` extension, which `az` offers to install on first use:

  ```bash
  SUB=$(az account show --query id -o tsv)
  az costmanagement query --type ActualCost --timeframe MonthToDate \
    --scope "/subscriptions/$SUB/resourceGroups/stockportfolio-rg" \
    --dataset-granularity Daily
  ```

  Untested against this subscription — the portal path above is the one that has been used.

Billing data lags roughly a day, so read a few days rather than yesterday alone, and remember the group
deletes itself on the `deleteAfter` tag, which caps the total whatever the daily rate turns out to be.

## Cost ceiling — do not remove

Pay-as-you-go has **no Azure spending limit**, and a budget only emails; it cannot stop anything.
The ceiling is enforced by time:

- `deploy.yml` stamps `deleteAfter = today + TEARDOWN_DAYS` (14) on the resource group.
- `teardown.yml` runs daily at 03:00 UTC and deletes the group once that date passes.
- **An unreadable or missing tag deletes.** Deliberate: a group with no readable deadline is a
  group nothing is bounding.

Re-deploying pushes the date out. To rebuild after a teardown, just trigger the deploy — it
provisions from empty.

## Secrets

Nine GitHub repo secrets; six are real credentials (the six Postgres passwords), the other three
are Azure identifiers. Auth to Azure is an **OIDC federated credential — there is no client
secret**. `JWT_SIGNING_KEY` was the tenth until Phase 6 and is gone: nothing ever read the `Jwt`
section, because sessions are ASP.NET Core Identity bearer tokens, which are data-protected rather
than signed. Delete it from the repository secrets when convenient — it is now inert either way.

Secrets reach `az` through an ARM parameter file, never as command-line arguments. If you add a
parameter, add it to the `jq` block in *Write deployment parameters*, not to the `az` argument list
— an argument is readable from `/proc/<pid>/cmdline`, and a step-level `env:` block does **not**
fix that, because the shell expands before `exec`.

### `FINNHUB_API_KEY` — the optional tenth

**Set**, so the deployed dashboard serves genuine prices and `GET /api/marketdata/health` returns
`{"provider":"Finnhub"}`. Empty is a *supported* path, not a broken one — it is what makes
`docker compose up` work from a clean clone with no registration — but on a public URL it reads as
broken rather than as a thoughtful fallback.

To rotate or re-set it, get a key at `finnhub.io` (free tier: 60 calls/minute, 30/second burst), then:

```bash
gh secret set FINNHUB_API_KEY --repo Dilicidum/StockPortfolio
```

It prompts for the value on stdin, so the key never lands in shell history or in a process argument
list. Setting a secret does **not** redeploy — the value is read at deploy time, so trigger a run
afterwards.

Two things only a real key can show, and both are confirmed on the live API: `GET
/api/marketdata/health` naming `Finnhub` instead of `Fake`, and a symbol that does not exist
returning `UnknownTicker` — `POST /api/holdings` with `ZQXW` gives 400 and *"'ZQXW' is not a ticker
this application recognises."* `FakeQuoteProvider` accepts any well-shaped ticker by design, so it
can never produce the second one. That check can only be made against the deployed app.

## Traps that each cost a deploy cycle

| Symptom | Cause |
|---|---|
| `AADSTS700213` at login | GitHub issues **immutable OIDC subjects**: `repo:<owner>@<ownerId>/<repo>@<repoId>:ref:refs/heads/main`. The documented short form matches nothing. Read the subject from the failing log and register it. |
| `MissingSubscriptionRegistration` | A new subscription registers almost no resource providers. `az provider show` returns region lists even for unregistered ones, so availability checks pass while deploys fail. |
| `AppLogsConfiguration.Destination is invalid` | The error lists `none` as valid while refusing it. It means the property **omitted**, not the string. |
| `ParentResourceNotFound` on `redisEnterprise/databases` | `existing` + `listKeys()` creates no dependency on the module that builds the resource. Build connection strings **inside** the module, as a `@secure()` output. Passes on retry — which is why it nearly shipped. |
| `--rule-name` unrecognised | `az postgres flexible-server firewall-rule` uses `--server-name` for the server and `--name` for the rule. |

More in the Traps section of [../CLAUDE.md](../CLAUDE.md).

## Known state

- **`minReplicas: 1`, and it must stay there.** `main.bicep` passes 1 and
  `containerapp-api.bicep` defaults to 1. It was 0 for cost through Phases 1–3, and Phase 4's quote
  poller ended that: a sleeping replica samples no prices, so no alert ever fires and nothing reports
  a fault. **The rule now runs the other way round** — search `src/` for `BackgroundService`,
  `IHostedService` and `PeriodicTimer`, and while anything matches, 1 is correct. `QuotePoller` is a
  `BackgroundService`, so something matches today. Do not "restore" 0 to save money; the poller and
  the always-on replica are one decision, and reverting either alone leaves a feature that stops
  working whenever traffic does.
- Managed identity covers ACR pull only; Postgres and Redis use passwords by design.
- No Key Vault — Container App secrets only.
- A smoke-test user (`smoke-*@example.com`) exists in the production database.
