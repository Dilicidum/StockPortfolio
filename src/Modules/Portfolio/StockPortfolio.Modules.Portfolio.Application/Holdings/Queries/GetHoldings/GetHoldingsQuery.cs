namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Queries.GetHoldings;

/// <summary>Every position this user holds.</summary>
public sealed record GetHoldingsQuery(Guid UserId);
