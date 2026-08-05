namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

/// <summary>A purchase folded into an existing position. The endpoint answers 200.</summary>
public sealed record HoldingMerged(HoldingSummary Holding);
