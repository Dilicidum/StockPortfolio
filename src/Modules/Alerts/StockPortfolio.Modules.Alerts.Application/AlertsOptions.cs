namespace StockPortfolio.Modules.Alerts.Application;

public sealed record AlertsOptions(
    int MaxWindowMinutes,
    TimeSpan Cooldown,
    int HistoryLimit,
    int MinimumSamples,
    TimeSpan MaxSampleGap)
{
    public const int DefaultMaxWindowMinutes = 60;

    public const int DefaultCooldownMinutes = 15;

    public const int DefaultHistoryLimit = 50;

    public const int DefaultMinimumSamples = 5;

    public const int DefaultPollIntervalSeconds = 60;

    public const int DefaultMaxMissedSamples = 3;
}
