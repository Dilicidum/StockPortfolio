namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

/// <summary>A position that did not exist before. The endpoint answers 201.</summary>
public sealed record HoldingCreated(HoldingSummary Holding);
