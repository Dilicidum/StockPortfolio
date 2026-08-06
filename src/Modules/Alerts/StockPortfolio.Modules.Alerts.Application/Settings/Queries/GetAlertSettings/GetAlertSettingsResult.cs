namespace StockPortfolio.Modules.Alerts.Application.Settings.Queries.GetAlertSettings;

/// <summary>One stored threshold. The percent is a number, not a string: the user typed it rather than the server computing it.</summary>
public sealed record GetAlertSettingsResult(
    string Ticker,
    decimal ThresholdPercent,
    int WindowMinutes,
    bool Enabled);
