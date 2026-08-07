namespace StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;

public sealed record SaveAlertSettingResult(
    string Ticker,
    decimal ThresholdPercent,
    int WindowMinutes,
    bool Enabled);
