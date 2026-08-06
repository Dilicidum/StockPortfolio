namespace StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;

/// <summary>The saved threshold, read back canonical — the ticker is upper-cased and the percent rounded.</summary>
public sealed record SaveAlertSettingResult(
    string Ticker,
    decimal ThresholdPercent,
    int WindowMinutes,
    bool Enabled);
