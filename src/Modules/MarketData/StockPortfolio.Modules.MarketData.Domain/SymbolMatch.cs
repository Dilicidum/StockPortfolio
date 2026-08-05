namespace StockPortfolio.Modules.MarketData.Domain;

/// <summary>One search hit: a symbol this app can actually hold, and the company behind it.</summary>
public readonly record struct SymbolMatch(Ticker Ticker, string Name);
