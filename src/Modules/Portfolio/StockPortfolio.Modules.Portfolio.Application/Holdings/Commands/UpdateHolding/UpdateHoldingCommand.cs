namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.UpdateHolding;

/// <summary>Corrects a mistyped position. Replaces quantity and price; never averages.</summary>
public sealed record UpdateHoldingCommand(Guid UserId, Guid HoldingId, decimal Quantity, decimal Price);
