namespace StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;

/// <summary>A window longer than the configured cap, which is longer than nothing kept in the price series.</summary>
public sealed record WindowExceedsRetention(int RequestedMinutes, int MaximumMinutes);
