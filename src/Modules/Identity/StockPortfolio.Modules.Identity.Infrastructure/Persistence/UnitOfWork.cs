using StockPortfolio.Modules.Identity.Application.Abstractions;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

internal sealed class UnitOfWork(IdentityDbContext context) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
        => await context.SaveChangesAsync(ct).ConfigureAwait(false);
}
