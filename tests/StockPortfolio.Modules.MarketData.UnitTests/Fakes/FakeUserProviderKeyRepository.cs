using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Tests.Fakes;

/// <summary>An in-memory user_provider_keys table, on the pattern of Alerts.UnitTests/Fakes.</summary>
internal sealed class FakeUserProviderKeyRepository : IUserProviderKeyRepository
{
    public List<UserProviderKey> Saved { get; } = [];

    public Task<UserProviderKey?> FindAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(Saved.Find(key => key.UserId == userId));

    public Task SaveAsync(UserProviderKey key, CancellationToken ct)
    {
        if (!Saved.Contains(key))
        {
            Saved.Add(key);
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(UserProviderKey key, CancellationToken ct)
    {
        Saved.Remove(key);

        return Task.CompletedTask;
    }

    public Task MarkRejectedAsync(Guid userId, CancellationToken ct)
    {
        Saved.Find(key => key.UserId == userId)?.MarkRejected(TimeProvider.System);

        return Task.CompletedTask;
    }
}
