namespace StockPortfolio.Modules.MarketData.Domain;

/// <summary>A price read back out of the fallback store, with the moment it was originally observed.</summary>
public readonly record struct LastPrice(decimal Price, DateTimeOffset ObservedAt);
