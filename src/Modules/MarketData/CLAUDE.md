# MarketData

Owns everything about prices coming in from outside: the current price of a ticker, whether a symbol
exists at all, the company's name, a short rolling history of prices, and each user's own provider
API key. Repo-wide rules are in the root [CLAUDE.md](../../../CLAUDE.md).

## What it persists

Schema `marketdata`, connection string name `MarketData`, database role `marketdata_svc`, context
`MarketDataDbContext`. Two tables: `user_provider_keys` (one encrypted key per user, plus the last
four characters and whether the provider rejected it) and `data_protection_keys` (the key ring that
seals both those secrets **and** every session token the app issues).

Everything price-shaped is in Redis:

| Key | Shape | Lifetime |
|---|---|---|
| `marketdata:last:{ticker}` | one value, `price:unixMillis` | never trimmed; the dashboard's only fallback |
| `marketdata:prices:{ticker}` | sorted set, member `unixMillis:price` | trimmed to the retention setting; the alert history |
| `marketdata:name:{ticker}` | company name | seven days |
| `marketdata:poll:last` | the last poll cycle's time and counts | — |
| `marketdata:claim:*`, `marketdata:cycle-inflight` | the two poll locks | short |

## What it publishes and consumes

It **references no other module**. Five interfaces in `.Contracts`: `IQuoteReader`,
`ICompanyNameReader`, `ISymbolValidator`, `IPriceWindowReader`, `IFeedHealth`.

It declares four ports of its own that something else fills, all in `Application/Abstractions`:

- `IPollTargetSource` — *which tickers am I to sample?* The host answers it from Alerts.
- `IPriceSampleObserver` — *a fresh sample landed.* The host forwards it to Alerts.
- `ISecretProtector` and `IKeyRingStore` — *encrypt this, and keep the keys.* The host implements
  them with ASP.NET Core Data Protection, because `.Infrastructure` may not reference ASP.NET Core.

Each is worded as this module's own need, never as "ask Alerts". Keep it that way.

## Where the interesting code is

| File | Why you would open it |
|---|---|
| `StockPortfolio.Modules.MarketData.Infrastructure/MarketDataModule.cs` | Which provider is registered, the resilience settings, and every port's default |
| `StockPortfolio.Modules.MarketData.Application/Prices/QuoteReader.cs` | The dashboard's read path: provider first, own key if any, last-known price only for what is missing |
| `StockPortfolio.Modules.MarketData.Domain/LastKnownPrice.cs` and `TradingClock.cs` | How old a stored price may be before it is hidden |
| `StockPortfolio.Modules.MarketData.Infrastructure/Polling/QuotePoller.cs` | The loop, its two locks, and the per-ticker isolation |
| `StockPortfolio.Modules.MarketData.Infrastructure/Quotes/FinnhubQuoteProvider.cs` | Every outbound call, and which of them fail open |
| `StockPortfolio.Modules.MarketData.Infrastructure/Quotes/FakeQuoteProvider.cs` | What runs with no key configured, including its invented key-checking rule |

## Gotchas

- **Every `IQuoteProvider` method fails open except `VerifyKeyAsync`.** `SymbolExistsAsync` answers
  *yes* when it cannot reach the provider, because an outage must not block a purchase. Checking a
  candidate key is the opposite: unanswerable is never accepted.
- **Which provider is registered changes its lifetime.** With a key it is a typed `HttpClient`, so
  **transient**; with no key `FakeQuoteProvider` is a singleton and is also registered as
  `IQuoteNudge`. `QuotePoller` is a singleton, so it resolves the provider from a per-cycle scope —
  never inject `IQuoteProvider` into a singleton.
- **A second named client, `FinnhubByok`, exists for per-user keys**, because the application's own
  key is baked into the typed client's default headers and cannot be swapped per request.
- **`IPriceSampleObserver` is registered with `TryAdd`,** so the no-op wins unless the host uses a
  plain `Add` afterwards. Get that wrong and nothing evaluates, no alert fires, and nothing fails.
- **The module injects `IConnectionMultiplexer` and nothing in it says so.** Redis must already be
  registered before `AddMarketDataModule` runs, or the dashboard fails on the first request.
- **A last-known price is shown only while at most sixty *open market* minutes have passed** since
  it was recorded — `TradingClock` counts trading minutes, so Friday's close stands all weekend.
  Holidays are not handled.
- **A price-window member carries its timestamp as well as its price.** A sorted set overwrites a
  duplicate member, so encoding the bare price would let a ticker that hits the same value twice
  erase its own earlier reading, invisibly to any test that asserts on prices.
- **`KeyRingStore` is a singleton that opens its own scope** to reach the context, and the host must
  call its data-protection wiring **after** `AddMarketDataModule`, since the protector depends on it.
- The name cache is written whenever a *search* sees a name; search results themselves are never
  cached, and a page render never asks the provider for a name.
