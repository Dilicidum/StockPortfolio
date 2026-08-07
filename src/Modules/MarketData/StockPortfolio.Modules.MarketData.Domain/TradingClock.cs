namespace StockPortfolio.Modules.MarketData.Domain;

/// <summary>Counts the minutes the US equity market was open between two instants, ignoring holidays.</summary>
public static class TradingClock
{
    private static readonly TimeSpan SessionOpen = new(9, 30, 0);

    private static readonly TimeSpan SessionClose = new(16, 0, 0);

    private static readonly TimeZoneInfo? NewYork = ResolveNewYork();

    public static double OpenMinutesBetween(DateTimeOffset from, DateTimeOffset to)
    {
        if (NewYork is null || to <= from)
        {
            return 0d;
        }

        var newYork = NewYork;
        var firstDay = TimeZoneInfo.ConvertTime(from, newYork).Date;
        var lastDay = TimeZoneInfo.ConvertTime(to, newYork).Date;
        var minutes = 0d;

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var opened = Instant(newYork, day + SessionOpen);
            var closed = Instant(newYork, day + SessionClose);

            var start = from > opened ? from : opened;
            var end = to < closed ? to : closed;

            if (end > start)
            {
                minutes += (end - start).TotalMinutes;
            }
        }

        return minutes;
    }

    // 09:30 and 16:00 never fall in a daylight-saving gap, so the offset for that wall time is always real.
    private static DateTimeOffset Instant(TimeZoneInfo newYork, DateTime newYorkLocal) =>
        new(newYorkLocal, newYork.GetUtcOffset(newYorkLocal));

    private static TimeZoneInfo? ResolveNewYork()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // Which id resolves depends on the operating system; try both before giving up.
            }
        }

        return null;
    }
}
