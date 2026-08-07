namespace StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;

public sealed record WindowExceedsRetention(int RequestedMinutes, int MaximumMinutes);
