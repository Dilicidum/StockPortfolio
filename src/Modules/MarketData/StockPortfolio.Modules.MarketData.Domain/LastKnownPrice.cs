namespace StockPortfolio.Modules.MarketData.Domain;

public static class LastKnownPrice
{
    private const double MaxOpenMinutes = 60d;

    private static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    // Nothing this old can be inside the hour, and the store accepts whatever epoch Redis holds — a corrupt
    // one would otherwise make the day-by-day walk below count from 1970 on every dashboard render.
    private static readonly TimeSpan WalkLimit = TimeSpan.FromDays(30);

    public static bool IsWorthShowing(LastPrice? price, DateTimeOffset now) =>
        price is { } p
        && p.Price > 0m
        && p.ObservedAt <= now + FutureTolerance
        && p.ObservedAt >= now - WalkLimit
        && TradingClock.OpenMinutesBetween(p.ObservedAt, now) <= MaxOpenMinutes;
}
