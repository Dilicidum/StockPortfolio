using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Abstractions;

public interface IAlertCooldownStore
{
    Task<bool> TryStartAsync(
        Guid userId,
        string ticker,
        AlertDirection direction,
        TimeSpan cooldown,
        CancellationToken ct);
}
