namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.SetHoldingVisibility;

public sealed record SetHoldingVisibilityCommand(Guid UserId, Guid HoldingId, bool IsVisible);
