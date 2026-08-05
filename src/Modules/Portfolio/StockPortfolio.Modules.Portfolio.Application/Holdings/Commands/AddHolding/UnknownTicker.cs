namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

/// <summary>A symbol this application will not accept: the shape is wrong, or the provider does not know it.</summary>
public sealed record UnknownTicker(string Ticker);
