using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Application.Abstractions;

/// <summary>Every write method here commits before it returns.</summary>
public interface IHoldingRepository
{
    Task<Holding?> FindAsync(Guid userId, Ticker ticker, CancellationToken ct);

    Task<Holding?> FindByIdAsync(Guid userId, HoldingId id, CancellationToken ct);

    Task<IReadOnlyList<Holding>> ListAsync(Guid userId, CancellationToken ct);

    Task AddAsync(Holding holding, CancellationToken ct);

    Task UpdateAsync(Holding holding, CancellationToken ct);

    Task RemoveAsync(Holding holding, CancellationToken ct);
}
