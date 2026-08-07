namespace StockPortfolio.Modules.MarketData.Domain;

public readonly record struct LastPrice(decimal Price, DateTimeOffset ObservedAt);
