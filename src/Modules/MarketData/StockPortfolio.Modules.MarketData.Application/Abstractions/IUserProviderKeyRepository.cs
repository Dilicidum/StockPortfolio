using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>Persists a user's own provider key. One row per user; repositories save their own changes.</summary>
public interface IUserProviderKeyRepository
{
    Task<UserProviderKey?> FindAsync(Guid userId, CancellationToken ct);

    // Inserts or updates the row and commits.
    Task SaveAsync(UserProviderKey key, CancellationToken ct);

    // Removes the row and commits.
    Task RemoveAsync(UserProviderKey key, CancellationToken ct);
}
