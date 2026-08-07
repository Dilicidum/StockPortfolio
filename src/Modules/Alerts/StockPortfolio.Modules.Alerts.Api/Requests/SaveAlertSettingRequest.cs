namespace StockPortfolio.Modules.Alerts.Api.Requests;

public sealed record SaveAlertSettingRequest(
    string Ticker,
    decimal ThresholdPercent,
    int WindowMinutes,
    bool Enabled);
