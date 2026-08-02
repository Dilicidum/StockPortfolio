using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

internal sealed class RefreshTokenRepository(IdentityDbContext context) : IRefreshTokenRepository
{
    public async Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct)
        => await context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct)
            .ConfigureAwait(false);

    public Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        context.RefreshTokens.Add(token);
        return Task.CompletedTask;
    }
}
