using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Abstractions;

/// <summary>Stores and finds thresholds. Every write method here commits before it returns.</summary>
public interface IAlertSettingRepository
{
    /// <summary>Finds this user's threshold on a canonical ticker, tracked so the handler can adjust it.</summary>
    Task<AlertSetting?> FindAsync(Guid userId, string ticker, CancellationToken ct);

    /// <summary>Lists every threshold this user has set, enabled or not, ticker order.</summary>
    Task<IReadOnlyList<AlertSetting>> ListForUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Lists every enabled threshold across every user — what one evaluation cycle works through.</summary>
    Task<IReadOnlyList<AlertSetting>> ListEnabledAsync(CancellationToken ct);

    /// <summary>Lists the distinct tickers with at least one enabled threshold — the poll list, and nothing more.</summary>
    Task<IReadOnlyList<string>> ListEnabledTickersAsync(CancellationToken ct);

    /// <summary>Inserts a new threshold or persists changes to a tracked one, and commits either way.</summary>
    Task SaveAsync(AlertSetting setting, CancellationToken ct);
}
