using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Persistence;

internal sealed class UserProviderKeyRepository(MarketDataDbContext context) : IUserProviderKeyRepository
{
    // No AsNoTracking: ChangeTracker.Entries<T>() only sees tracked entities, so an untracked read means
    // Replace changes an object nobody saves, with no error at all.
    public Task<UserProviderKey?> FindAsync(Guid userId, CancellationToken ct) =>
        context.UserProviderKeys.FirstOrDefaultAsync(key => key.UserId == userId, ct);

    public async Task SaveAsync(UserProviderKey key, CancellationToken ct)
    {
        if (context.Entry(key).State == EntityState.Detached)
        {
            context.UserProviderKeys.Add(key);
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(UserProviderKey key, CancellationToken ct)
    {
        context.UserProviderKeys.Remove(key);
        await context.SaveChangesAsync(ct);
    }
}
