using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Abstractions;

/// <summary>Appends breaches and reads them back. The write commits before it returns.</summary>
public interface IFiredAlertRepository
{
    /// <summary>Records one breach and commits. A fired alert is never updated afterwards.</summary>
    Task AddAsync(FiredAlert alert, CancellationToken ct);

    /// <summary>Reads this user's newest alerts first, capped at limit — the only way the table is read.</summary>
    Task<IReadOnlyList<FiredAlert>> ListRecentAsync(Guid userId, int limit, CancellationToken ct);
}
