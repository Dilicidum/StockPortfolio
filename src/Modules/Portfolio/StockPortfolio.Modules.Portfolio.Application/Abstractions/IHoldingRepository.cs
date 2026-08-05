using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Application.Abstractions;

/// <summary>Stores and finds holdings. Every write method here commits before it returns.</summary>
public interface IHoldingRepository
{
    /// <summary>Finds this user's position in a ticker, tracked so the handler can mutate it.</summary>
    Task<Holding?> FindAsync(Guid userId, Ticker ticker, CancellationToken ct);

    /// <summary>Finds one of this user's holdings by id. Scoped to the user: another user's id is not found.</summary>
    Task<Holding?> FindByIdAsync(Guid userId, HoldingId id, CancellationToken ct);

    /// <summary>Lists this user's holdings, newest position first.</summary>
    Task<IReadOnlyList<Holding>> ListAsync(Guid userId, CancellationToken ct);

    /// <summary>Inserts a holding and commits.</summary>
    Task AddAsync(Holding holding, CancellationToken ct);

    /// <summary>Persists changes made to a tracked holding and commits.</summary>
    Task UpdateAsync(Holding holding, CancellationToken ct);

    /// <summary>Deletes a holding and commits.</summary>
    Task RemoveAsync(Holding holding, CancellationToken ct);
}
