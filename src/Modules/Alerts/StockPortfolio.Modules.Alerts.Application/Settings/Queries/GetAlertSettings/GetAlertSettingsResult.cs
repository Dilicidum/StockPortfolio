namespace StockPortfolio.Modules.Alerts.Application.Settings.Queries.GetAlertSettings;

public sealed record GetAlertSettingsResult(
    string Ticker,
    decimal ThresholdPercent,
    int WindowMinutes,
    bool Enabled);
