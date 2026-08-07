namespace StockPortfolio.Modules.MarketData.Domain;

/// <summary>The three-state poll verdict as a pure function, so it is testable with neither Redis nor a host.</summary>
public static class FeedHealthRule
{
    private const int HealthyIntervals = 3;

    private const int DegradedIntervals = 10;

    public static FeedVerdict Evaluate(
        DateTimeOffset? lastCycleAt,
        int tickersTargeted,
        int tickersStored,
        TimeSpan interval,
        DateTimeOffset now)
    {
        if (lastCycleAt is not { } finishedAt)
        {
            return FeedVerdict.Unhealthy;
        }

        var age = now - finishedAt;

        if (age > interval * DegradedIntervals)
        {
            return FeedVerdict.Unhealthy;
        }

        var punctual = age <= interval * HealthyIntervals;

        if (tickersTargeted == 0)
        {
            return punctual ? FeedVerdict.Healthy : FeedVerdict.Degraded;
        }

        if (tickersStored == 0)
        {
            return FeedVerdict.Degraded;
        }

        return punctual ? FeedVerdict.Healthy : FeedVerdict.Degraded;
    }
}
