# Phase 5 — Make it mine

## What you can do at the end

Flip the app to dark. Switch it to Ukrainian. Set the dashboard to refresh every fifteen seconds and watch it
speed up. Set an alert threshold to 2%. Hide a position you don't want cluttering the screen. Paste in your
own market-data API key and have the app use it for you.

This covers the brief's settings requirement in full, plus the bring-your-own-key extra.

**On "a list of stocks".** The brief asks for *dashboard settings — a list of stocks, quote refresh
frequency*. The list sits inside dashboard settings, so it means which of your positions appear on your
dashboard. It is not a separate watchlist of shares you don't own. That reading costs one flag on a position.
The other reading costs a new aggregate, a second table, a merged ticker list and a second dashboard section
with no profit and loss in it — for something the brief never asked for.

---

## Who owns a setting

Settings are split across the modules that own the thing being configured. A single shared settings table
would be a piece of the system nobody designed and everybody writes to.

| Setting | Owner |
|---|---|
| Theme, language | Identity |
| Refresh interval, position visibility | Portfolio |
| Alert threshold, window, on/off | Alerts |
| Your own provider API key | MarketData |

The browser reads all of it in one request and writes each section separately. One read, targeted writes.

⚠️ **Check before you start: Alerts is a module in these documents and does not exist on disk.** Phase 4
builds it. If Phase 4 has slipped when this phase begins, the alert threshold has nowhere to live — and the
answer is to resolve that, not to work around it by parking the threshold in Portfolio. That was tried as a
design once and reversed.

---

## The settings themselves

### Theme and language

A small preferences record beside the user, holding a theme (light, dark or follow the system) and a language
(English or Ukrainian). It is created the first time it is read, using the defaults, so registration stays a
single insert and nobody has to remember to seed it.

### Position visibility

Hiding a position is a **display filter and nothing else**. The flag already exists on a position, already
defaults to visible, and the dashboard read already honours it — so this phase adds the switch, not the
filtering.

What must not happen is visibility leaking anywhere else:

- **Alerts ignore it.** You still own the position. A 6% drop still matters to your money whether or not the
  row is on your screen. This is worth a line in the README, because it is the first thing a reviewer asks.
- **It has nothing to do with what gets polled.** Phase 4's poller runs for tickers somebody has an active
  alert on, not for everything anybody holds. So a hidden position with no alert on it is never sampled, and
  that is correct rather than a regression — the dashboard prices it on the very next load, from the
  provider, the moment you unhide it.

### Refresh interval

A number of seconds, between 10 and 300, default 60.

**Say plainly what it does.** The dashboard asks the provider for prices when the browser asks the server, so
a fifteen-second interval genuinely fetches four times as often as a sixty-second one. Every refresh is a
real fan-out — one provider call per visible position. Twenty positions is twenty calls out of a free tier of
roughly sixty a minute, for one viewer, at the default. At fifteen seconds a single viewer exhausts the
budget alone.

So whatever the settings screen says about this, it must not imply that a faster refresh is free. It costs
the shared quota, and the person spending it is not the only person using it.

⚠️ **Do not lower the 60-second default to make that arithmetic look better.** The default is load-bearing
for the capacity claim in Phase 3 and for the README paragraph built on it. The 10-to-300 range is not the
problem — what the screen *claims* is.

### Your own API key

The brief asks for it, and it is genuinely awkward against a shared key.

The key is stored server-side, encrypted at rest, and used only for **that user's own dashboard fetches**.
The shared poll cycle behind alerts keeps using the application's key.

Why not a per-user poller: polling per user multiplies cycles by the number of users, needs a rate limiter
and a claim per user, and buys nothing, because the polled ticker list is shared — two people with an alert
on the same ticker would fetch it twice. The dashboard read, by contrast, is *already* per user and per
request, which is exactly the shape a per-user key needs.

**The key is never returned to the browser.** Not to the person who set it, not masked beyond the last four
characters. The settings response says whether one is configured and shows the last four, and that is all.
There is no product reason to read it back, and every path that can return it is a path that can leak it.

It is validated on save with one live call to the provider, so a bad key is rejected while the user is
looking at the field rather than silently at three in the morning.

**Two consequences worth planning for, not discovering:**

- **This gives MarketData its first database table.** The module stores nothing in the database today — no
  context, no migration, nothing in the list of modules the migrator runs. Its schema and its database login
  already exist and sit unused. Storing user keys is what makes them real, and it means a first migration, a
  new entry in the migrator's list, and an update to the check that pins which schemas have migration history
  — after Phase 4 has already added the alerts schema to that same check. It is a task, not a detail.
- **The provider rate limiter is process-wide and sized for one shared key.** A user supplying their own key
  brings their own quota, and should not be spending the application's. Either the limiter becomes
  per-key, or bring-your-own-key users are throttled against a budget that isn't theirs. Decide which before
  writing the storage.

Encryption uses the framework's data-protection keys, and **those keys must be persisted to Postgres**. The
default keeps them in the container filesystem, so every new deployment revision would generate a fresh key
ring and turn every stored key into ciphertext nothing can read.

---

## The API

```
GET    /api/settings                  everything, in one read
PATCH  /api/settings/appearance       theme, language
PATCH  /api/settings/dashboard        refresh interval
PATCH  /api/settings/alerts           enabled, threshold, window
PATCH  /api/holdings/{id}/visibility  show or hide one position
POST   /api/settings/api-key          validates against the provider, then stores
DELETE /api/settings/api-key
```

All authenticated. Everything that takes a value can reject it with a validation error.

---

## The screen

One settings route, in the mockup's order: appearance, language, quotes (refresh interval and threshold),
your own key, then the dashboard's visibility list.

**Each section saves on its own**, with its own inline saved-or-failed state. One big form with a single Save
button would let a rejected API key throw away a perfectly good theme change.

### Theme

Three-way: light, dark, or follow the system. Following the system means watching the OS preference and
reacting to it live, so a user who changes their laptop theme sees the app follow without reloading.

⚠️ **No flash on load.** A blocking inline script, before any stylesheet, reads the stored choice and applies
it to the document. Without it, every page load flashes light before the app mounts — visible on every
navigation, and exactly the kind of thing a reviewer notices without being able to name.

The server value is the source of truth across devices; browser storage is only the bootstrap cache, so the
inline script has something to read synchronously.

Note that Tailwind v4 has **no config file** — dark mode is declared as a variant in CSS. Every pre-2025
tutorial gets this wrong, and the failure is silent: the dark styles simply never apply.

### Language

English and Ukrainian; the brief asks for at least two. Translations are grouped per feature, mirroring the
backend's features. Phase 2's form validation already stores message *keys* rather than English strings, so
validation messages translate with no changes to the forms. Numbers and dates go through the browser's
locale-aware formatting; the currency stays US dollars and only the presentation localises.

**Add a build check that both language files have exactly the same set of keys.** A missing Ukrainian key
renders as a raw key path in the UI, which looks far worse than English would — and falling back to English
hides the bug from you while showing it to everyone else.

Two things that bite: the language detector caches to browser storage by default and will quietly override
the server's value on the next load, so configure it to let the server win once you're signed in and treat
the cache as a pre-sign-in bootstrap only. And react-i18next 17 needs i18next 26.2 or later — the mismatch
fails at runtime, not at install.

### Visibility and the key field

Visibility is a checkbox list of your positions with a "showing 6 of 8" counter and a Show all link. Hidden
rows disappear from the dashboard table.

The key field is a password input showing "configured, ends a1b2" once set, with a Remove button. Saving
shows a spinner while the server checks the key against the live API, then either succeeds or gives a
specific reason for the rejection.

---

## Infrastructure

Nothing new to provision — the third phase in a row where front-loading the infrastructure means a feature
costs no infrastructure work. Two additions: data-protection keys persisted to Postgres, and a switch that
turns bring-your-own-key off in one place if it misbehaves, set the same way locally and in Azure. The
language key-parity check joins the existing build job.

---

## One decision left to you

**Do hidden positions count toward the dashboard totals?** Someone hides two of their eight positions. What
do the headline numbers say?

- **Totals include everything.** Hiding is purely visual; your portfolio is worth what it's worth. But the
  visible rows then don't add up to the total on screen, which reads as an arithmetic bug, and someone will
  report it as one.
- **Totals follow visibility.** What you see adds up. Internally consistent — but "total value $12,400" is
  now not your portfolio's value, and someone who hid a position months ago has a permanently wrong number in
  front of them with nothing explaining why.
- **Both.** Visible totals as the headline, with a quiet "8 positions, $18,900 including hidden" underneath.
  Honest, costs one line of UI, and explains itself.

Whichever you pick, a position's weight must be computed against the **same** total that is displayed, or the
percentages won't sum to 100 and that really is a bug.

---

## Done when

- `docker compose up`, sign in, open the settings screen.
- Switch to dark — applies instantly. Reload — **no flash**, still dark.
- Choose "follow the system", change the OS theme — the app follows without a reload.
- Switch to Ukrainian — navigation, tables, buttons, validation messages and number and date formats all
  translate. Reload — still Ukrainian.
- Set the refresh to 15 seconds — the dashboard visibly refetches faster, and the screen is honest that each
  refetch is a real round of provider calls against a shared quota.
- Set a threshold to 2% — Phase 4's alerts respect it.
- Hide a position — its row disappears, the counter updates, and the totals behave the way you decided above.
- Confirm hiding changed nothing but the display: nudge the hidden ticker past its threshold and it still
  alerts. Set the alert up first, since nothing is sampled for a ticker with no alert on it.
- Paste an invalid provider key — rejected, with a specific message.
- Paste a valid one — stored, shown as configured with its last four characters, and **never** visible in the
  browser's network responses.
- Backend and frontend test suites green, including the language key-parity check.
- Deployed, and all of the above works on the public URL.
- README covers: who owns which setting, what a shorter refresh interval actually costs, how a user's own key
  is used and where the application's key is still used, and what hiding a position does and does not affect.
- The settings screen is usable at 375px wide.

## Reference

These describe the shape of the system rather than the order it gets built in. They live in `docs/reference/`.

- [Data model](../reference/er-diagram.md) — the settings tables and the per-user key table, which is the price module's first.
- [Module boundaries](../reference/module-boundaries.md) — which module owns which setting.
