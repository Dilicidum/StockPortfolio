namespace StockPortfolio.Modules.Alerts.Application;

/// <summary>The Alerts module's tunable numbers, read once at startup and injected as one value.</summary>
public sealed record AlertsOptions(int MaxWindowMinutes)
{
    /// <summary>A move measured over days is a trend, not a sharp move, so an hour is the ceiling.</summary>
    public const int DefaultMaxWindowMinutes = 60;
}
