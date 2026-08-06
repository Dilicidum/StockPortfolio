namespace StockPortfolio.Modules.Alerts.Api.Requests;

/// <summary>The body of PUT /api/alerts/settings. The user comes from the bearer token, never from here.</summary>
public sealed record SaveAlertSettingRequest(
    string Ticker,
    decimal ThresholdPercent,
    int WindowMinutes,
    bool Enabled);
