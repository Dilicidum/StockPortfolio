using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

internal sealed class UserPreferencesRepository(IdentityDbContext context) : IUserPreferencesRepository
{
    public Task<UserPreferences?> FindAsync(string userId, CancellationToken ct) =>
        context.UserPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task SaveAsync(UserPreferences preferences, CancellationToken ct)
    {
        if (context.Entry(preferences).State == EntityState.Detached)
        {
            context.UserPreferences.Add(preferences);
        }

        await context.SaveChangesAsync(ct);
    }
}
