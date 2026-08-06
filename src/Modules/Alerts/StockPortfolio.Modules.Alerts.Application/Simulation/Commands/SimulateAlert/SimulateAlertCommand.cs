namespace StockPortfolio.Modules.Alerts.Application.Simulation.Commands.SimulateAlert;

/// <summary>Fires one alert on demand. An absent ticker lets the server pick one of the caller's.</summary>
public sealed record SimulateAlertCommand(Guid UserId, string? Ticker);
