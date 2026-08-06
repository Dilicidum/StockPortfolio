using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Application.Abstractions;

public interface IDashboardSettingsRepository
{
    Task<DashboardSettings?> FindAsync(Guid userId, CancellationToken ct);

    // Inserts or updates the row and commits.
    Task SaveAsync(DashboardSettings settings, CancellationToken ct);
}
