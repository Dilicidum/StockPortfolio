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

        // Staleness is judged first: an idle poller still writes a heartbeat every cycle, so one that stopped
        // arriving says the poller stopped, whatever the cycle it describes happened to contain.
        if (age > interval * DegradedIntervals)
        {
            return FeedVerdict.Unhealthy;
        }

        var punctual = age <= interval * HealthyIntervals;

        // A cycle with nothing to poll is a working poller that nobody has set an alert on, not a broken feed.
        if (tickersTargeted == 0)
        {
            return punctual ? FeedVerdict.Healthy : FeedVerdict.Degraded;
        }

        // Punctual and storing nothing is a dead feed; timing alone would show a green light on it.
        if (tickersStored == 0)
        {
            return FeedVerdict.Degraded;
        }

        return punctual ? FeedVerdict.Healthy : FeedVerdict.Degraded;
    }
}
