namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.SetHoldingVisibility;

/// <summary>Shows or hides a position on the dashboard.</summary>
public sealed record SetHoldingVisibilityCommand(Guid UserId, Guid HoldingId, bool IsVisible);
