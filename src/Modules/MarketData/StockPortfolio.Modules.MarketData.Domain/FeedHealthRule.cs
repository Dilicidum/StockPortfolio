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

        // A cycle with nothing to poll is a working poller that nobody has set an alert on, not a broken feed.
        if (tickersTargeted == 0)
        {
            return FeedVerdict.Healthy;
        }

        // A cycle that asked for prices and stored none is a dead feed however punctual it is; timing alone
        // reports a provider outage as healthy, because the poller keeps finishing cycles that fetch nothing.
        if (tickersStored == 0)
        {
            return FeedVerdict.Degraded;
        }

        var age = now - finishedAt;

        if (age <= interval * HealthyIntervals)
        {
            return FeedVerdict.Healthy;
        }

        return age <= interval * DegradedIntervals ? FeedVerdict.Degraded : FeedVerdict.Unhealthy;
    }
}
