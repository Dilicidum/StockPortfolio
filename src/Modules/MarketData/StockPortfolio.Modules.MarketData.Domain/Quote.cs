namespace StockPortfolio.Modules.MarketData.Domain;

/// <summary>One price for one symbol, stamped with when this app observed it — never the provider's trade time.</summary>
public readonly record struct Quote(Ticker Ticker, decimal Price, DateTimeOffset ObservedAt);
