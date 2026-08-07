namespace StockPortfolio.Modules.Alerts.Application.Simulation.Commands.SimulateAlert;

public sealed record SimulateAlertCommand(Guid UserId, string? Ticker);
