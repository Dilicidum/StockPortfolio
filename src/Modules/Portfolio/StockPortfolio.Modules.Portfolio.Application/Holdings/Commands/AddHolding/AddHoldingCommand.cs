namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;

/// <summary>Buys a quantity of a ticker, opening or adding to a position.</summary>
public sealed record AddHoldingCommand(Guid UserId, string Ticker, decimal Quantity, decimal Price);
