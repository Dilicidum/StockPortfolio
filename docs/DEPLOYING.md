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
| Burn rate | ~$1.26/day |
| Running | **Phase 4, deployed and verified 2026-08-06** (PR #6, run 31087381000) |
| Deletes itself on | **`deleteAfter`** — expected 2026-08-20 after that deploy, but **read the group's tag**; this line is a note, not the value |

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
curl -s -o /dev/null -w '%{http_code}\n' <API_URL>/health/ready     # expect 200
```

A green run is not proof. `/health/ready` returning 200 is, because it touches Postgres and Redis.
For a real check, `POST /api/auth/register` then `GET /api/auth/me` with the bearer.

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

Ten GitHub repo secrets; seven are real credentials (six Postgres passwords + JWT signing key), the
other three are Azure identifiers. Auth to Azure is an **OIDC federated credential — there is no
client secret**.

Secrets reach `az` through an ARM parameter file, never as command-line arguments. If you add a
parameter, add it to the `jq` block in *Write deployment parameters*, not to the `az` argument list
— an argument is readable from `/proc/<pid>/cmdline`, and a step-level `env:` block does **not**
fix that, because the shell expands before `exec`.

### `FINNHUB_API_KEY` — the optional eleventh

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

- `minReplicas: 0` for cost. **Set it back to `1` in `main.bicep` when MarketData ships its quote
  poller**, or the polling stops whenever traffic does. That is Phase 4. Check the condition, not the
  phase number: search `src/` for `BackgroundService`, `IHostedService` and `PeriodicTimer` — no hits
  today, so `0` is right. `containerapp-api.bicep` carries a comment saying `minReplicas: 1` is
  needed because of that poller; that comment describes the finished system, and `main.bicep` is the
  line that actually passes `0`.
- Managed identity covers ACR pull only; Postgres and Redis use passwords by design.
- No Key Vault — Container App secrets only.
- A smoke-test user (`smoke-*@example.com`) exists in the production database.
