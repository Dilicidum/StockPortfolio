namespace StockPortfolio.Modules.Portfolio.Api.Requests;

// The body of PUT /api/settings/dashboard. A plain JSON number: the user typed it, not the server.
public sealed record SaveDashboardSettingsRequest(int RefreshIntervalSeconds);
