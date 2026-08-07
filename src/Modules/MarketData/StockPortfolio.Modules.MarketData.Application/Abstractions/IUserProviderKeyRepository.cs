using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

public interface IUserProviderKeyRepository
{
    Task<UserProviderKey?> FindAsync(Guid userId, CancellationToken ct);

    Task SaveAsync(UserProviderKey key, CancellationToken ct);

    Task RemoveAsync(UserProviderKey key, CancellationToken ct);

    Task MarkRejectedAsync(Guid userId, CancellationToken ct);
}
