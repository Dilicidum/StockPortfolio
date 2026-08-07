namespace StockPortfolio.Modules.Portfolio.Api.Requests;

public sealed record AddHoldingRequest(string Ticker, decimal Quantity, decimal Price);
