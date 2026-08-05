namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

/// <summary>A symbol this application will not accept. Phase 2 checks shape; Phase 3 checks existence.</summary>
public sealed record UnknownTicker(string Ticker);
