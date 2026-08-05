namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.RemoveHolding;

/// <summary>Closes a position.</summary>
public sealed record RemoveHoldingCommand(Guid UserId, Guid HoldingId);
