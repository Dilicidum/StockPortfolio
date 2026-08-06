using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Abstractions;

/// <summary>The one thing standing between a threshold and an alert every cycle it stays breached.</summary>
public interface IAlertCooldownStore
{
    /// <summary>Sets the cooldown only if absent, and reports whether this caller won. One round trip.</summary>
    Task<bool> TryStartAsync(
        Guid userId,
        string ticker,
        AlertDirection direction,
        TimeSpan cooldown,
        CancellationToken ct);
}
