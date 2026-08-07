namespace StockPortfolio.Modules.MarketData.Domain;

public readonly record struct Quote(Ticker Ticker, decimal Price, DateTimeOffset ObservedAt);
