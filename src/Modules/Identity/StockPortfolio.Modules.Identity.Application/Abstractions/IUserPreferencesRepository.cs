using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Abstractions;

public interface IUserPreferencesRepository
{
    Task<UserPreferences?> FindAsync(UserId userId, CancellationToken ct);

    // Inserts or updates the row and commits.
    Task SaveAsync(UserPreferences preferences, CancellationToken ct);
}
