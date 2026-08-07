using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

internal sealed class HoldingRepository(PortfolioDbContext context) : IHoldingRepository
{
    // No AsNoTracking anywhere here: an untracked read means a command handler saves nothing, silently.
    public async Task<Holding?> FindAsync(Guid userId, Ticker ticker, CancellationToken ct)
        => await context.Holdings.FirstOrDefaultAsync(h => h.UserId == userId && h.Ticker == ticker, ct);

    public async Task<Holding?> FindByIdAsync(Guid userId, HoldingId id, CancellationToken ct)
        => await context.Holdings.FirstOrDefaultAsync(h => h.UserId == userId && h.Id == id, ct);

    public async Task<IReadOnlyList<Holding>> ListAsync(Guid userId, CancellationToken ct)
        => await context.Holdings
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(Holding holding, CancellationToken ct)
    {
        context.Holdings.Add(holding);
        await context.SaveChangesAsync(ct);
    }

    // Takes the holding it does not use: the parameter names the aggregate being persisted at the call site.
    public async Task UpdateAsync(Holding holding, CancellationToken ct)
        => await context.SaveChangesAsync(ct);

    public async Task RemoveAsync(Holding holding, CancellationToken ct)
    {
        context.Holdings.Remove(holding);
        await context.SaveChangesAsync(ct);
    }
}
