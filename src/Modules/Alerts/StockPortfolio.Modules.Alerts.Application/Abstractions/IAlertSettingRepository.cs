using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Abstractions;

public interface IAlertSettingRepository
{
    Task<AlertSetting?> FindAsync(Guid userId, string ticker, CancellationToken ct);

    Task<IReadOnlyList<AlertSetting>> ListForUserAsync(Guid userId, CancellationToken ct);

    Task<IReadOnlyList<AlertSetting>> ListEnabledForTickerAsync(string ticker, CancellationToken ct);

    Task<IReadOnlyList<string>> ListEnabledTickersAsync(CancellationToken ct);

    Task SaveAsync(AlertSetting setting, CancellationToken ct);
}
