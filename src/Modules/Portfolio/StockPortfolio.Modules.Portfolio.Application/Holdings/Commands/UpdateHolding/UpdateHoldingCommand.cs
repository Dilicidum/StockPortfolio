namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.UpdateHolding;

public sealed record UpdateHoldingCommand(Guid UserId, Guid HoldingId, decimal Quantity, decimal Price);
