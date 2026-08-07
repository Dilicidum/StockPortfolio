namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.RemoveHolding;

public sealed record RemoveHoldingCommand(Guid UserId, Guid HoldingId);
