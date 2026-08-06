namespace StockPortfolio.Modules.Alerts.Application.Settings.Queries.GetAlertSettings;

/// <summary>Every threshold one user has set, switched on or off.</summary>
public sealed record GetAlertSettingsQuery(Guid UserId);
