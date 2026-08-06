namespace StockPortfolio.Modules.Portfolio.Application.Dashboard.Commands.SaveDashboardSettings;

public sealed record SaveDashboardSettingsCommand(Guid UserId, int RefreshIntervalSeconds);
