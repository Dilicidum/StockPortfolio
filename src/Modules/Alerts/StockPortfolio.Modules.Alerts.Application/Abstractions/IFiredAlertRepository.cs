using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Abstractions;

public interface IFiredAlertRepository
{
    Task AddAsync(FiredAlert alert, CancellationToken ct);

    Task<IReadOnlyList<FiredAlertRow>> ListRecentAsync(Guid userId, int limit, CancellationToken ct);
}
