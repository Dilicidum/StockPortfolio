namespace StockPortfolio.Modules.Alerts.Application.History.Queries.GetFiredAlerts;

public sealed record GetFiredAlertsQuery(Guid UserId, int Limit);
