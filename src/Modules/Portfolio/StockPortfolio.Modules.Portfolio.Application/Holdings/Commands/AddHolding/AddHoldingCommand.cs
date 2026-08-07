namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

public sealed record AddHoldingCommand(Guid UserId, string Ticker, decimal Quantity, decimal Price);
