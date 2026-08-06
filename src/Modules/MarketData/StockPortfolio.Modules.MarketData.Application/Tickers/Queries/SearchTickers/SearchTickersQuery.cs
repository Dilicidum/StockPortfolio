namespace StockPortfolio.Modules.MarketData.Application.Tickers.Queries.SearchTickers;

/// <summary>Whatever the user has typed so far. Anything at all is a legal query, including nonsense.</summary>
public sealed record SearchTickersQuery(string? Query);
