using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Application.Abstractions;

/// <summary>SaveAsync inserts or updates the row and commits before it returns.</summary>
public interface IDashboardSettingsRepository
{
    Task<DashboardSettings?> FindAsync(Guid userId, CancellationToken ct);

    Task SaveAsync(DashboardSettings settings, CancellationToken ct);
}
