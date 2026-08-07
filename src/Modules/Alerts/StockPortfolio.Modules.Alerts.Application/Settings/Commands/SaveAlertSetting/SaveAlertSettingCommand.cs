namespace StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;

public sealed record SaveAlertSettingCommand(
    Guid UserId,
    string Ticker,
    decimal ThresholdPercent,
    int WindowMinutes,
    bool Enabled);
