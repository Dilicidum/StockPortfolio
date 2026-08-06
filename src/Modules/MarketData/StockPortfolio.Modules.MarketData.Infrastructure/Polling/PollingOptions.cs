using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Polling;

/// <summary>How often the poller samples and how long a series is kept. Silent defaults; nothing here throws.</summary>
internal sealed class PollingOptions
{
    /// <summary>The configuration section these values are read from.</summary>
    public const string SectionName = "MarketData:Polling";

    private const int DefaultIntervalSeconds = 60;

    private const int DefaultRetentionMinutes = 75;

    private PollingOptions(TimeSpan interval, TimeSpan retention)
    {
        Interval = interval;
        Retention = retention;
    }

    /// <summary>The gap between cycles, and the unit both lease expiries are measured in.</summary>
    public TimeSpan Interval { get; }

    /// <summary>How far back a price window is kept; the store trims to it on every write.</summary>
    public TimeSpan Retention { get; }

    /// <summary>Reads the section. A missing or unreadable value falls back rather than taking the host down.</summary>
    public static PollingOptions FromConfiguration(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var section = config.GetSection(SectionName);

        return new PollingOptions(
            TimeSpan.FromSeconds(Positive(section["IntervalSeconds"], DefaultIntervalSeconds)),
            TimeSpan.FromMinutes(Positive(section["RetentionMinutes"], DefaultRetentionMinutes)));
    }

    /// <summary>Zero and negative are as unusable as unparseable, so all three take the default.</summary>
    private static int Positive(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : fallback;
}
