namespace StockPortfolio.Modules.Alerts.Application;

/// <summary>The Alerts module's tunable numbers, read once at startup and injected as one value.</summary>
public sealed record AlertsOptions(
    int MaxWindowMinutes,
    TimeSpan Cooldown,
    int HistoryLimit,
    int MinimumSamples,
    TimeSpan MaxSampleGap)
{
    /// <summary>A move measured over days is a trend, not a sharp move, so an hour is the ceiling.</summary>
    public const int DefaultMaxWindowMinutes = 60;

    /// <summary>Long enough that a nudge twice inside it yields one alert, short enough to demo.</summary>
    public const int DefaultCooldownMinutes = 15;

    /// <summary>The ceiling on the limit GET /api/alerts accepts.</summary>
    public const int DefaultHistoryLimit = 50;

    /// <summary>One stale point is not a window.</summary>
    public const int DefaultMinimumSamples = 5;

    /// <summary>The poller's own default, and the unit both feed guards are measured in.</summary>
    public const int DefaultPollIntervalSeconds = 60;

    /// <summary>Three missed polls is a gap in the feed; two is a cycle that ran late.</summary>
    public const int DefaultMaxMissedSamples = 3;
}
