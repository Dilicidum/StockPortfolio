using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

internal sealed class RefreshTokenRepository(IdentityDbContext context) : IRefreshTokenRepository
{
    public async Task<RefreshToken?> FindByHashAsync(byte[] tokenHash, CancellationToken ct)
        => await context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    // Every repository in the module shares one scoped DbContext, so this commit also carries whatever the
    // handler changed on an entity it had already loaded. That is what replaced the explicit unit of work.
    public async Task AddAsync(RefreshToken token, CancellationToken ct)
    {
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(RefreshToken token, CancellationToken ct)
    {
        context.RefreshTokens.Update(token);
        await context.SaveChangesAsync(ct);
    }
}
