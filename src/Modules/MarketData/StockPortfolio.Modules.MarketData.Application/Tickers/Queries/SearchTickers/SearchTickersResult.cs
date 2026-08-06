namespace StockPortfolio.Modules.MarketData.Application.Tickers.Queries.SearchTickers;

/// <summary>One suggestion. The query returns a list of these, so a run of nothing is an empty list.</summary>
public sealed record SearchTickersResult(string Symbol, string Description);
