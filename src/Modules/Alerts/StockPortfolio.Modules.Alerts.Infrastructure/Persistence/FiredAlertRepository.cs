using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence;

internal sealed class FiredAlertRepository(AlertsDbContext context) : IFiredAlertRepository
{
    public async Task AddAsync(FiredAlert alert, CancellationToken ct)
    {
        context.FiredAlerts.Add(alert);
        await context.SaveChangesAsync(ct);
    }

    // AsNoTracking here and nowhere else in this module: history is read and rendered, never changed.
    public async Task<IReadOnlyList<FiredAlert>> ListRecentAsync(Guid userId, int limit, CancellationToken ct)
        => await context.FiredAlerts
            .AsNoTracking()
            .Where(alert => alert.UserId == userId)
            .OrderByDescending(alert => alert.FiredAt)
            .Take(limit)
            .ToListAsync(ct);
}
