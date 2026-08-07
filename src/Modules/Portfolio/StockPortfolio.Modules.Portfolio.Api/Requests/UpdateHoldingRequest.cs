namespace StockPortfolio.Modules.Portfolio.Api.Requests;

public sealed record UpdateHoldingRequest(decimal Quantity, decimal Price);
