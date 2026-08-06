namespace StockPortfolio.Modules.MarketData.Infrastructure.Polling;

/// <summary>The two locks a cycle needs. A seam, so the poller's own tests need no Redis to run a cycle.</summary>
internal interface IPollLease
{
    /// <summary>True only when both locks are held; false means some other replica owns this cycle.</summary>
    Task<bool> TryAcquireAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>Hands back the in-flight flag only. The minute claim is left to expire, so a minute runs once.</summary>
    Task ReleaseAsync(CancellationToken ct);
}
