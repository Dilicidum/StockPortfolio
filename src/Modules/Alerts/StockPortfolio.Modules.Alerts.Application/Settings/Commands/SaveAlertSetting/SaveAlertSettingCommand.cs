namespace StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;

/// <summary>Sets or changes one threshold. The user comes from the bearer token, never from the body.</summary>
public sealed record SaveAlertSettingCommand(
    Guid UserId,
    string Ticker,
    decimal ThresholdPercent,
    int WindowMinutes,
    bool Enabled);
