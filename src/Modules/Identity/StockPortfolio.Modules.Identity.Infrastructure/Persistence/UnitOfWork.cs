using StockPortfolio.Modules.Identity.Application.Abstractions;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

/// <inheritdoc cref="IUnitOfWork"/>
/// <remarks>
/// A thin wrapper over the context on purpose: it is what lets a handler in <c>.Application</c> commit
/// without naming <see cref="IdentityDbContext"/>, which is internal to this assembly.
/// </remarks>
internal sealed class UnitOfWork(IdentityDbContext context) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct)
        => await context.SaveChangesAsync(ct).ConfigureAwait(false);
}
