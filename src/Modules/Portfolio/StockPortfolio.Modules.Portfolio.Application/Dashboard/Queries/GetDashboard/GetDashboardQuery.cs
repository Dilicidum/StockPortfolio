namespace StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboard;

/// <summary>Asks for one user's dashboard: their visible positions, priced and totalled.</summary>
public sealed record GetDashboardQuery(Guid UserId);
