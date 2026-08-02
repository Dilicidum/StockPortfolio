namespace StockPortfolio.Shared.Kernel.Cqrs;

/// <summary>A single rule failure, carried as a case of a handler's result union or returned by a domain.</summary>
public sealed record ValidationFailed(string Field, string Message);
