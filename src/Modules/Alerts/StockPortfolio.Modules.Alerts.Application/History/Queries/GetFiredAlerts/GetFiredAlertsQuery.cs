namespace StockPortfolio.Modules.Alerts.Application.History.Queries.GetFiredAlerts;

/// <summary>Asks for one user's recent alerts. The limit is clamped, never rejected.</summary>
public sealed record GetFiredAlertsQuery(Guid UserId, int Limit);
