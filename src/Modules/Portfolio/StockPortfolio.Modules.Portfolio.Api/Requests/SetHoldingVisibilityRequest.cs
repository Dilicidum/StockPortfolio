namespace StockPortfolio.Modules.Portfolio.Api.Requests;

/// <summary>The body of PATCH /api/holdings/{id}/visibility.</summary>
public sealed record SetHoldingVisibilityRequest(bool IsVisible);
