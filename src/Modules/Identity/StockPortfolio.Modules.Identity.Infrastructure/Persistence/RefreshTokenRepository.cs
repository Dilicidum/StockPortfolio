using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

/// <inheritdoc cref="IRefreshTokenRepository"/>
internal sealed class RefreshTokenRepository(IdentityDbContext context) : IRefreshTokenRepository
{
    /// <remarks>
    /// Tracked, not <c>AsNoTracking</c>: rotation loads the token and then calls
    /// <c>Supersede(...)</c> on it, and the change has to be picked up by the unit of work.
    /// </remarks>
    public async Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct)
        => await context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct)
            .ConfigureAwait(false);

    /// <remarks>
    /// Does not save. Issuing a token is always part of a larger unit of work — a login writes one
    /// token, a rotation writes one and supersedes another — so <see cref="IUnitOfWork"/> owns the
    /// commit. <c>DbSet.Add</c> is synchronous by design; the async overload only matters for value
    /// generators that hit the database, and the id is generated in the domain.
    /// </remarks>
    public Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        context.RefreshTokens.Add(token);
        return Task.CompletedTask;
    }
}
