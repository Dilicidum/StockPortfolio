using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

internal sealed class DashboardSettingsRepository(PortfolioDbContext context) : IDashboardSettingsRepository
{
    public Task<DashboardSettings?> FindAsync(Guid userId, CancellationToken ct) =>
        context.DashboardSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);

    public async Task SaveAsync(DashboardSettings settings, CancellationToken ct)
    {
        if (context.Entry(settings).State == EntityState.Detached)
        {
            context.DashboardSettings.Add(settings);
        }

        await context.SaveChangesAsync(ct);
    }
}
