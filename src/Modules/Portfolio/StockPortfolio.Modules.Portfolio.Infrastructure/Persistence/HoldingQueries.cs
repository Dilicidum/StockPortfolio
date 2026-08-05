using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

/// <summary>The read another module needs, taking and returning primitives at the boundary.</summary>
internal sealed class HoldingQueries(PortfolioDbContext context) : IUserHoldsTicker
{
    /// <inheritdoc/>
    public async Task<bool> HoldsAsync(Guid userId, string ticker, CancellationToken ct)
    {
        // Parsed rather than trusted: the argument crosses a module boundary as a bare string, and a
        // symbol that is not a ticker at all is "not held", not an exception on a read.
        if (!Ticker.Create(ticker).TryPickT0(out var parsed, out _))
        {
            return false;
        }

        // AsNoTracking is correct here and only here: this is a read model nothing mutates. The
        // repository must never gain it. No visibility filter either - a hidden position is still held.
        return await context.Holdings
            .AsNoTracking()
            .AnyAsync(h => h.UserId == userId && h.Ticker == parsed, ct);
    }
}
