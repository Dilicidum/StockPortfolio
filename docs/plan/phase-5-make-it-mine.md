# Phase 5 — Make it mine · 0.6 days

## 1. Goal

Flip to dark. Switch to Ukrainian. Set refresh to 15s and watch the dashboard speed up. Set the threshold to 2%. Hide a position you don't want cluttering the dashboard. Paste your own Finnhub key.

Covers P1 req 8 in full and P2 req 11.

Near-greenfield: `Initial.md` gives settings three words in a build-order line (`:196`) and **hard-codes the 60-second cadence in two places** (`:66`, `:150`) that this phase is meant to make configurable.

**On «перелік акцій».** Req 8 reads *«налаштування дашборду (перелік акцій, частота оновлення котирувань)»* — dashboard settings, a list of stocks, the refresh frequency. It sits inside *dashboard settings*, so it means which stocks appear on your dashboard, not a separate watchlist of stocks you don't own. That is an `is_visible` flag on `holdings`, not a new aggregate, a child table, a union in the poll set, and a second dashboard section with no P&L in it.

---

## 2. Backend

Settings are split across the modules that own them. A single `user_settings` table would be a fifth module nobody designed.

| Setting | Owner |
|---|---|
| Theme, language | `Identity` |
| Refresh interval, holding visibility | `Portfolio` |
| Threshold, window, enabled | `Alerts` (settled in Phase 4) |
| BYOK key | `MarketData` |

The frontend fetches one aggregated `GET /api/settings` view and PATCHes each section separately — one read, targeted writes.

### 2.1 `UserPreferences` — `Identity.Domain`

```csharp
public sealed class UserPreferences
{
    private UserPreferences(UserPreferencesId id, UserId userId, Theme theme, Language language);

    public UserPreferencesId Id { get; private set; }
    public UserId UserId { get; private set; }
    public Theme Theme { get; private set; }        // Light | Dark | System
    public Language Language { get; private set; }  // En | Uk

    public OneOf<Success, InvalidInput> Update(Theme theme, Language language);
}
```

No base class, and the entity declares its own `Id` — see `phase-1-implementation.md` §5.2.

Created lazily on first read with defaults `System` / `En`, so registration stays a single insert.

### 2.2 Holding visibility

One method on the existing `Holding` aggregate from Phase 2:

```csharp
public void SetVisible(bool visible);   // no validation to fail — a plain state change
```

No new aggregate, no new table, no events. `is_visible` defaults to `true`, so every existing holding keeps working with no migration data step.

**The poll set ignores it.** Phase 3 polls all held tickers regardless of visibility — hiding a position must not stop its price being collected, or unhiding it would show a stale number until the next cycle. Visibility is a display filter and nothing more.

**Alerts ignore it too.** You still own the position; a 6% drop still matters to your money whether or not the row is on screen. Worth a README line, because it is the first thing a reviewer will ask.

### 2.3 `DashboardSettings` — `Portfolio.Domain`

```csharp
public int RefreshIntervalSeconds { get; private set; }   // 10 .. 300
```

**This is a client-side polling cadence, not a server one.** The server keeps polling at 60s regardless, so a user picking 15s gets the same observation four times. The freshness timestamp must make that visible, otherwise the UI implies four fresh fetches and quietly lies.

Surface it: when `refreshInterval < serverPollInterval`, the settings screen shows an inline note — *"Prices are collected every 60s. A faster refresh re-reads the same data sooner, it does not fetch more often."* That sentence is worth more than the feature.

### 2.4 BYOK — `MarketData`

The brief's req 11 and the mockup both have it, and it is genuinely awkward against a single shared-key poller. `Initial.md` does not mention it at all.

The user's key is stored **server-side, encrypted at rest**, and used only for that user's **read-through** calls. The shared poll cycle keeps using the app key.

Why this rather than a per-user poller: per-user polling multiplies cycles by user count, needs a per-user rate limiter and a per-user claim, and buys nothing, because the poll set is shared and two users holding AAPL would fetch it twice. Read-through is already per-request and already rate-limited, so it is the natural seam.

Encrypted with ASP.NET Core Data Protection, keys persisted to Postgres via `PersistKeysToDbContext` — the default stores them in the container filesystem, so every ACA revision would generate a new key ring and turn every stored BYOK key into undecryptable ciphertext.

**Never returned to the browser.** `GET /api/settings` returns `{ byok: { configured: true, lastFour: "a1b2" } }` and nothing more. Validated on save with a single live `/quote` call, so a bad key is rejected at the point of entry rather than silently at 3am.

### 2.5 Endpoints

```
GET    /api/settings                200 + AggregatedSettingsDto      [Authorize]
PATCH  /api/settings/appearance     200 | 400     { theme, language }
PATCH  /api/settings/dashboard      200 | 400     { refreshIntervalSeconds }
PATCH  /api/settings/alerts         200 | 400     { enabled, thresholdPercent, windowMinutes }
PATCH  /api/holdings/{id}/visibility 204 | 404    { visible }
POST   /api/settings/api-key        200 | 400     { key }   → validates, then stores
DELETE /api/settings/api-key        204
```

---

## 3. Frontend

### Route

`src/routes/_authenticated/settings.tsx` — the mockup's Settings screen, in its order: **Appearance** · **Language** · **Quotes** (refresh interval, threshold) · **BYOK** · **Dashboard** (the visibility list, where the mockup put its stock list).

Each section saves independently with its own mutation and inline saved/failed state. One giant form with a single Save button would make a failed BYOK validation discard a perfectly good theme change.

### Theme

Tailwind v4 dark mode is a `@custom-variant`, **not** `darkMode: 'class'` — that config key does not exist in v4, and there is no config file to put it in.

```css
@import "tailwindcss";
@custom-variant dark (&:where([data-theme="dark"], [data-theme="dark"] *));
```

Three-way Light / Dark / System. System resolves via `matchMedia('(prefers-color-scheme: dark)')` with a listener, so changing the OS theme updates a `System` user live.

⚠️ **No flash on load.** A blocking inline script in `index.html`, before any CSS, reads the persisted choice and stamps `data-theme` on `<html>`. Without it every page load flashes light before React hydrates — visible on every navigation, and exactly the kind of thing a reviewer notices without being able to name.

The server value is the source of truth across devices; `localStorage` is the bootstrap cache so the inline script has something synchronous to read.

### i18n

`react-i18next` + `i18next-browser-languagedetector`, which gives persistence for free. EN and UK — the brief requires a minimum of two.

Namespaces per feature (`auth`, `portfolio`, `dashboard`, `alerts`, `settings`, `common`), mirroring the backend modules. Phase 2's zod schemas already store message **keys** rather than strings, so validation messages translate with no changes to the schemas. Numbers and dates go through `Intl.NumberFormat` and `Intl.DateTimeFormat` with the active locale — currency stays USD, only the formatting localises.

Add a CI check that both locale files have identical key sets. A missing Ukrainian key renders as the raw key path in the UI, which looks far worse than an English fallback, and `fallbackLng` hides the bug from you while showing it to everyone else.

⚠️ Pin `i18next >= 26.2.0`; `react-i18next@17` requires it and the mismatch fails at runtime, not at install.

### Dashboard visibility

A checkbox list of the user's holdings with a "showing 6 of 8" counter and a Show all link. Hidden rows disappear from the dashboard table.

### BYOK

Password-type input, "configured · ends a1b2" state, Remove button. Saving shows a spinner while the server validates against the live API, then a clear success or a specific failure ("key rejected by Finnhub").

---

## 4. Infrastructure delta

Nothing new to provision. Two additions worth a comment in the Bicep: Data Protection keys live in Postgres, so a redeploy does not orphan stored BYOK secrets; and `MarketData__AllowUserApiKeys=true` as an env var, so BYOK can be switched off in one place if it misbehaves. The same var goes in compose. The locale key-parity check joins the CI job.

Third phase in a row where front-loading the infrastructure means a feature costs zero infrastructure work.

---

## 5. Tests

### Unit

| Test | Asserts |
|---|---|
| `Preferences_InvalidLanguage_Rejected` | |
| `DashboardSettings_IntervalBelowTen_Rejected` | Lower bound |
| `DashboardSettings_IntervalAboveThreeHundred_Rejected` | Upper bound |
| `Holding_SetVisible_TogglesFlag` | |
| `ApiKey_EncryptedAtRest_CiphertextDiffersFromPlaintext` | |
| `ApiKey_RoundTripsThroughDataProtection` | |

### Integration — `Api.IntegrationTests`

| Test | Asserts |
|---|---|
| `Settings_RoundTrip_AllSections` | PATCH then GET returns what was written |
| `Settings_PartialFailure_DoesNotDiscardOtherSections` | A bad BYOK key leaves theme intact |
| `HiddenHolding_ExcludedFromDashboardRows` | The feature |
| `HiddenHolding_StillInPollSet` | Hiding must not stop price collection |
| `HiddenHolding_StillTriggersAlerts` | You still own it |
| `ApiKey_NeverAppearsInAnyResponseBody` | Scans the full JSON of every settings endpoint |
| `ApiKey_InvalidOnSave_Returns400` | Validated at entry |
| `ApiKey_Configured_UsedForReadThroughOnly` | The poll cycle still uses the app key |
| `RefreshInterval_DoesNotChangeServerPollCadence` | The honest-labelling rule, enforced |

### Frontend

`theme toggle updates data-theme and persists across reload` · `no flash: data-theme is set before first paint` · `system theme follows matchMedia changes` · `language switch re-renders translated strings` · `locale files have identical key sets` · `interval change updates refetchInterval without remount` · `BYOK input never renders the stored key` · `hiding a holding removes its row and updates the counter`

---

## 6. Gotchas

**Tailwind v4 has no config file.** `darkMode: 'class'` does not exist; dark mode is `@custom-variant` in CSS. Every pre-2025 tutorial is wrong, and the failure mode is silent — `dark:` classes simply never apply.

**The no-flash script must be blocking and inline**, in `index.html` before the stylesheet. Moving it into React, or adding `defer`, brings the flash straight back.

**`i18next-browser-languagedetector` caches to `localStorage` by default**, which will silently override the server value on next load. Configure `detection.order` so the server value wins once authenticated, and treat the cache as a pre-auth bootstrap only.

**Data Protection keys must be persisted** or every ACA revision orphans every stored BYOK key. `PersistKeysToDbContext` into the `identity` schema.

**Do not return the BYOK key, ever** — not even to the user who set it, not even masked beyond the last four. There is no product reason to read it back, and every path that can return it is a path that can leak it.

---

## 7. Your call

### Do hidden holdings count toward the totals? — `Portfolio.Application/DashboardProjection.cs`

```csharp
// TODO(you): a user hides two of their eight positions. What do the KPI tiles say?
//
//   (a) TOTALS INCLUDE EVERYTHING — hiding is purely visual. Your portfolio is
//       worth what it's worth. But the visible rows then don't add up to the
//       total on screen, which reads as an arithmetic bug, and someone will
//       report it as one.
//
//   (b) TOTALS FOLLOW VISIBILITY — what you see adds up. Internally consistent,
//       but "Total value $12,400" is now not your portfolio's value, and a user
//       who hid something months ago has a permanently wrong number in front
//       of them with nothing indicating why.
//
//   (c) BOTH — visible totals as the headline, with a muted "8 positions,
//       $18,900 including hidden" beneath. Honest, costs one extra line of UI,
//       and makes the choice self-explaining.
//
// Whichever you pick, weight (position ÷ total) must use the SAME denominator,
// or the percentages won't sum to 100 and that really is a bug.
```

About eight lines. Decide before writing `HiddenHolding_ExcludedFromDashboardRows`, since the totals assertion goes in the same test.

---

## 8. Done when

- [ ] `docker compose up`, log in, open `/settings`
- [ ] Toggle Dark → applies instantly; reload → **no flash**, still dark
- [ ] Set System, change the OS theme → the app follows without a reload
- [ ] Switch to Ukrainian → nav, tables, buttons, validation messages and number/date formats all translate
- [ ] Reload → still Ukrainian
- [ ] Set refresh to 15s → dashboard visibly refetches faster, and the "collected every 60s" note is shown
- [ ] Set threshold to 2% → Phase 4's alerts respect it
- [ ] Hide a position → its row disappears, the counter updates, and the totals behave the way you decided in §7
- [ ] Confirm the hidden ticker is still polled (`redis-cli ZCARD marketdata:prices:{ticker}` keeps growing)
- [ ] Nudge the hidden ticker past the threshold → it still alerts
- [ ] Paste an invalid Finnhub key → rejected with a specific message
- [ ] Paste a valid one → stored, shown as "configured · ends ****", and **never** visible in devtools network responses
- [ ] `dotnet test` green, including `HiddenHolding_StillInPollSet` and `ApiKey_NeverAppearsInAnyResponseBody`
- [ ] `npm test` green, including the locale key-parity check
- [ ] Deployed; all of the above on the GitHub Pages URL
- [ ] README: the settings ownership split, why the refresh interval is client-side only, the BYOK read-through design, and what hiding a holding does and doesn't affect
- [ ] Settings screen usable at 375px
