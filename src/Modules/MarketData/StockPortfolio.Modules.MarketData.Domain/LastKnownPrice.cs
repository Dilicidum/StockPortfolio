namespace StockPortfolio.Modules.MarketData.Domain;

public static class LastKnownPrice
{
    private const double MaxOpenMinutes = 60d;

    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    // Caps the day-by-day walk below: the store accepts whatever epoch Redis holds, and a corrupt one would count from 1970 on every render.
    private static readonly TimeSpan WalkLimit = TimeSpan.FromDays(30);

    public static bool IsWorthShowing(LastPrice? price, DateTimeOffset now) =>
        price is { } p
        && p.Price > 0m
        && p.ObservedAt <= now + FutureTolerance
        && p.ObservedAt >= now - WalkLimit
        && TradingClock.OpenMinutesBetween(p.ObservedAt, now) <= MaxOpenMinutes;
}
