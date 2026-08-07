namespace StockPortfolio.Modules.MarketData.Domain;

public static class LastKnownPrice
{
    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    public static bool IsWorthShowing(LastPrice? price, DateTimeOffset now) =>
        price is { } p && p.Price > 0m && p.ObservedAt <= now + FutureTolerance;
}
