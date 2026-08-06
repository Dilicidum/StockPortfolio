namespace StockPortfolio.Modules.Alerts.Application.Simulation.Commands.SimulateAlert;

/// <summary>Nothing to simulate: no enabled threshold at all, or none on the ticker that was named.</summary>
public sealed record NoPositionToSimulate(string? Ticker);
