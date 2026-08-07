using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>SaveAsync inserts or updates the row and commits before it returns.</summary>
public interface IUserPreferencesRepository
{
    Task<UserPreferences?> FindAsync(Guid userId, CancellationToken ct);

    Task SaveAsync(UserPreferences preferences, CancellationToken ct);
}
