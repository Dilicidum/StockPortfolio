using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Polling;

internal sealed class PollingOptions
{
    public const string SectionName = "MarketData:Polling";

    private const int DefaultIntervalSeconds = 60;

    private const int DefaultRetentionMinutes = 75;

    private PollingOptions(TimeSpan interval, TimeSpan retention)
    {
        Interval = interval;
        Retention = retention;
    }

    public TimeSpan Interval { get; }

    public TimeSpan Retention { get; }

    public static PollingOptions FromConfiguration(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var section = config.GetSection(SectionName);

        return new PollingOptions(
            TimeSpan.FromSeconds(Positive(section["IntervalSeconds"], DefaultIntervalSeconds)),
            TimeSpan.FromMinutes(Positive(section["RetentionMinutes"], DefaultRetentionMinutes)));
    }

    private static int Positive(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;
}
