namespace StockPortfolio.Modules.MarketData.Domain;

/// <summary>Whether a stored observation is sound enough to render. Age is deliberately not part of it.</summary>
public static class LastKnownPrice
{
    /// <summary>Clock skew tolerated before an observation is treated as corrupt rather than future.</summary>
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    /// <summary>Age never disqualifies a price — the reader judges that from the timestamp we render.</summary>
    public static bool IsWorthShowing(LastPrice? price, DateTimeOffset now) =>
        price is { } p && p.Price > 0m && p.ObservedAt <= now + FutureTolerance;
}
