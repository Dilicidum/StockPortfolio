namespace StockPortfolio.Shared.Kernel;

/// <summary>A single rule failure, carried as a case of a handler's result union or returned by a domain factory.</summary>
public sealed record InvalidInput(string Field, string Message);
