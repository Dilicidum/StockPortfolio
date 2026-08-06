namespace StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;

/// <summary>A threshold was set on a symbol the caller holds no position in, hidden or otherwise.</summary>
public sealed record TickerNotHeld(string Ticker);
