namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>The trimmed alert series, kept apart from the last-known store because their lifetimes differ.</summary>
public interface IPriceWindowStore
{
    /// <summary>Appends one sample and drops anything older than retention. Best-effort: it never throws out.</summary>
    Task AppendAsync(
        string ticker,
        decimal price,
        DateTimeOffset at,
        TimeSpan retention,
        CancellationToken ct);

    /// <summary>Reads every sample at or after since, oldest first. A ticker with no series reads as empty.</summary>
    Task<IReadOnlyList<(DateTimeOffset At, decimal Price)>> ReadAsync(
        string ticker,
        DateTimeOffset since,
        CancellationToken ct);
}
