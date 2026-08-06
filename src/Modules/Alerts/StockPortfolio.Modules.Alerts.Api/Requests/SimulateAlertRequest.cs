namespace StockPortfolio.Modules.Alerts.Api.Requests;

/// <summary>The body of POST /api/alerts/simulate. Null ticker means "pick one of mine".</summary>
public sealed record SimulateAlertRequest(string? Ticker);
