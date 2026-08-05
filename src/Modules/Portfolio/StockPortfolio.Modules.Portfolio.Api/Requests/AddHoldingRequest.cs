namespace StockPortfolio.Modules.Portfolio.Api.Requests;

/// <summary>The body of POST /api/holdings. The user comes from the bearer token, never from here.</summary>
public sealed record AddHoldingRequest(string Ticker, decimal Quantity, decimal Price);
