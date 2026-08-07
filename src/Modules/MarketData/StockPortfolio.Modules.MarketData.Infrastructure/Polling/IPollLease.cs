namespace StockPortfolio.Modules.MarketData.Infrastructure.Polling;

internal interface IPollLease
{
    Task<bool> TryAcquireAsync(DateTimeOffset now, CancellationToken ct);

    Task ReleaseAsync(CancellationToken ct);
}
