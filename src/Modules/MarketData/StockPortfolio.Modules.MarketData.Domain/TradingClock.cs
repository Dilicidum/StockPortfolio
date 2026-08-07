namespace StockPortfolio.Modules.MarketData.Domain;

/// <summary>Counts the minutes the US equity market was open between two instants.</summary>
public static class TradingClock
{
    private static readonly TimeSpan SessionOpen = new(9, 30, 0);

    private static readonly TimeSpan SessionClose = new(16, 0, 0);

    private static readonly TimeZoneInfo NewYork = ResolveNewYork();

    /// <summary>Minutes of open market between the two instants, ignoring holidays.</summary>
    public static double OpenMinutesBetween(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            return 0d;
        }

        var firstDay = TimeZoneInfo.ConvertTime(from, NewYork).Date;
        var lastDay = TimeZoneInfo.ConvertTime(to, NewYork).Date;
        var minutes = 0d;

        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var opened = Instant(day + SessionOpen);
            var closed = Instant(day + SessionClose);

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
    private static DateTimeOffset Instant(DateTime newYorkLocal) =>
        new(newYorkLocal, NewYork.GetUtcOffset(newYorkLocal));

    // Invariant globalization removes ICU, and which of these two ids resolves then depends on the operating system.
    private static TimeZoneInfo ResolveNewYork()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }
}
