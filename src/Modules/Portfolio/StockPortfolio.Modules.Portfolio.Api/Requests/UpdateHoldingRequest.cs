namespace StockPortfolio.Modules.Portfolio.Api.Requests;

/// <summary>The body of PATCH /api/holdings/{id}. It replaces the values; it is not a second purchase.</summary>
public sealed record UpdateHoldingRequest(decimal Quantity, decimal Price);
