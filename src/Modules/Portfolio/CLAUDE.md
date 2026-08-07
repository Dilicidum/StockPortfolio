# Portfolio

Owns what a user holds: one position per user and ticker, the purchase price averaged across buys,
and the dashboard that prices those positions and totals them. Also owns the dashboard's refresh
interval. Repo-wide rules are in the root [CLAUDE.md](../../../CLAUDE.md).

## What it persists

Schema `portfolio`, connection string name `Portfolio`, database role `portfolio_svc`, context
`PortfolioDbContext`.

| Table | Holds |
|---|---|
| `holdings` | one row per position, with a unique index `ix_holdings_user_id_ticker` |
| `dashboard_settings` | the refresh interval, keyed on user id, 10 to 300 seconds, default 60 |

Nothing of Portfolio's lives in Redis.

## What it publishes and consumes

Publishes one interface, `IUserHoldsTicker` in `.Contracts` — Alerts calls it to refuse a threshold
on a ticker you do not own.

Consumes three from `MarketData.Contracts`: `IQuoteReader` for prices, `ICompanyNameReader` for
company names, `ISymbolValidator` to check a symbol exists before a purchase is recorded. Three
separate interfaces on purpose — a price outage, a missing name and an unknown symbol are different
failures.

## Where the interesting code is

| File | Why you would open it |
|---|---|
| `StockPortfolio.Modules.Portfolio.Domain/Holding.cs` | Every rule about quantity, price and averaging, including the merge arithmetic |
| `StockPortfolio.Modules.Portfolio.Application/Dashboard/DashboardCalculator.cs` | All the money and percentage arithmetic, in one pure function |
| `StockPortfolio.Modules.Portfolio.Infrastructure/Persistence/HoldingQueries.cs` | The dashboard read, and the "do you hold this?" answer |
| `StockPortfolio.Modules.Portfolio.Application/Holdings/Commands/AddHolding/AddHoldingCommandHandler.cs` | Create-or-merge: the shape of every write in this module |
| `StockPortfolio.Modules.Portfolio.Infrastructure/Persistence/Configurations/HoldingConfiguration.cs` | Column names, precision, and how `Money` is mapped |
| `StockPortfolio.Modules.Portfolio.Api/PortfolioEndpoints.cs` | `/api/holdings`, `/api/dashboard`, `/api/settings/dashboard` |

## Gotchas

- **`Holding.AveragePrice` is assigned after construction, not passed to the constructor.** A
  complex type cannot be a constructor parameter (efcore#31621). Add it to the parameter list and
  the model fails to build at **host startup**, not on the first query.
- **`Money`'s own constructor uppercases the currency, and EF calls it once per row loaded.** That
  is why `HoldingQueries.GetVisibleHoldingsAsync` projects `AveragePrice.Amount` and `.Currency`
  separately and rebuilds `Money` in memory. Do not "simplify" that projection back.
- **`HoldingConfiguration` maps each `Money` member by hand inside the `ComplexProperty` lambda.**
  `Money.Amount` and `Money.Currency` have no setter, so a bare `ComplexProperty(h => h.AveragePrice)`
  maps nothing and throws at model build. A member added to `Money` later is silently unmapped until
  someone adds a `.Property()` line.
- **Adding a holding is create-or-merge, and the two answers differ on the wire.** A new position
  returns 201 with a `Location`; a second purchase of the same ticker weights the average price and
  returns 200. `PATCH` is a correction and averages nothing.
- **`Ticker` here has only `Create`, returning a `OneOf`.** MarketData's identically named type also
  has `TryParse`. Code copied between the two modules will not compile, which is the point.
- **`IUserHoldsTicker` ignores `IsVisible` deliberately** — a hidden position is still held, so an
  alert on it is legitimate. Only the dashboard filters on visibility.
- **The dashboard totals only the positions it could price.** An unpriced one is listed with nulls,
  and is left out of both totals and the weight denominator, so a missing price never reads as a
  loss. A portfolio mixing currencies throws rather than mislabelling a total.
- **`HoldingQueries` is registered twice**, as `IUserHoldsTicker` and as `IDashboardHoldingReader`.
- Every repository write commits before it returns; `IHoldingRepository`'s doc comment is the
  statement of that, and handlers rely on it.
