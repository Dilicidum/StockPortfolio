using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Infrastructure.Persistence;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>Reads a user's own key back in plaintext, resolved once per dashboard load by QuoteReader.</summary>
internal sealed class UserProviderKeyReader(MarketDataDbContext context, ISecretProtector protector)
    : IUserProviderKeyReader
{
    public async Task<string?> ReadPlaintextAsync(Guid userId, CancellationToken ct)
    {
        var key = await context.UserProviderKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.UserId == userId, ct);

        return key is null ? null : protector.Unprotect(key.Ciphertext);
    }
}
