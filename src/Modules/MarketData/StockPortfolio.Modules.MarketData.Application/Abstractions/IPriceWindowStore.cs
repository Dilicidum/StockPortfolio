namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

public interface IPriceWindowStore
{
    Task AppendAsync(
        string ticker,
        decimal price,
        DateTimeOffset at,
        TimeSpan retention,
        CancellationToken ct);

    Task<IReadOnlyList<(DateTimeOffset At, decimal Price)>> ReadAsync(
        string ticker,
        DateTimeOffset since,
        CancellationToken ct);
}
