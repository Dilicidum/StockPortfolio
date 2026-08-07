# Alerts

Owns the rule "tell me when this stock moves sharply": one threshold per user and ticker, the record
of every alert that fired, and the push that puts it in the browser straight away. Repo-wide rules
are in the root [CLAUDE.md](../../../CLAUDE.md).

## What it persists

Schema `alerts`, connection string name `Alerts`, database role `alerts_svc`, context
`AlertsDbContext`.

| Table | Holds |
|---|---|
| `alert_settings` | threshold percent, window in minutes, on/off — unique on user **and** ticker, so a threshold belongs to a position rather than to an account |
| `fired_alerts` | one row per alert, indexed on user and time, newest first |

One Redis key: `alerts:cooldown:{userId}:{ticker}:{direction}`, holding a marker for the length of
the cooldown. It is claimed in a single round trip with `When.NotExists` — a read followed by a write
lets two replicas both find it absent and send two alerts for one move.

## What it publishes and consumes

Publishes `IAlertEvaluator` and `IWatchedTickerReader` in `.Contracts`. Nothing calls them directly:
the host wraps each in a small adapter and hands it to MarketData, which asked for *which tickers do
I sample* and *a sample landed* in its own words.

Consumes `IPriceWindowReader` from `MarketData.Contracts` for price history, and `IUserHoldsTicker`
from `Portfolio.Contracts` to refuse a threshold on a ticker the user does not hold.

## Where the interesting code is

| File | Why you would open it |
|---|---|
| `StockPortfolio.Modules.Alerts.Application/Evaluation/AlertEvaluator.cs` | The whole decision: who is watching, is the feed usable, does the cooldown allow it |
| `StockPortfolio.Modules.Alerts.Application/Evaluation/MoveAssessment.cs` | The arithmetic of "a sharp move", in about thirty lines |
| `StockPortfolio.Modules.Alerts.Application/Streaming/AlertDispatcher.cs` | Save then push, and what happens when the push fails |
| `StockPortfolio.Modules.Alerts.Api/Streaming/SignalRAlertPublisher.cs` and `SubjectClaimUserIdProvider.cs` | How an alert reaches one browser and not the others |
| `StockPortfolio.Modules.Alerts.Infrastructure/Redis/RedisAlertCooldownStore.cs` | The one-round-trip claim, and why a Redis failure suppresses rather than resends |
| `StockPortfolio.Modules.Alerts.Infrastructure/AlertsModule.cs` | Every registration, and where the options really come from |

## Gotchas

- **The move is measured from the window's high or low, not from end to end.** Whichever of "fell
  from the high" and "rose from the low" is larger decides the direction. The endpoint change is
  computed too, and only used as a sign check: without it a price oscillating inside a band wider
  than the threshold fires against the opposite extreme every single cycle, for ever.
- **A stale feed suppresses alerts instead of firing them.** No samples, too few samples, too large
  a gap between them, or a newest sample older than the allowed gap — all mean silence. A feed that
  stopped is not a price that stopped moving.
- **A Redis failure also suppresses.** There is no way to tell whether this alert already went out,
  so retrying would turn a cache outage into duplicates.
- **The simulate button deliberately skips the cooldown**, so a button the user just pressed is never
  swallowed by a window that a real evaluation opened.
- **The publisher and the user-id provider are registered by `AddAlertsApi`, not `AddAlertsModule`.**
  `IHubContext` is ASP.NET Core, and `.Infrastructure` may not reference it. This module needs three
  host calls, not two.
- **`Clients.User(...)` matches whatever `IUserIdProvider` returns.** The built-in one reads
  `nameidentifier`; these tokens carry `sub`. Without `SubjectClaimUserIdProvider` every alert is
  delivered to nobody, with no exception and no log line.
- **The hub lives at `/api/alerts/stream`** and takes its token from `?access_token=`, because a
  browser cannot set a header on a WebSocket.
- **`AlertsOptions` reads the *poller's* configuration keys** for the minimum sample count and the
  allowed gap. They are measured in poll intervals, so a private copy would drift and silently stop
  alerts firing.
- **`ListEnabledTickersAsync` materialises the rows before reading `.Value`.** EF cannot see inside a
  value-converted type, so projecting the inner string in the query fails at run time, not at model
  build.
- The dispatcher saves the alert first and only then pushes. A failed push is logged and the alert
  still arrives on the next history read; that is the whole of how a missed message is recovered.
