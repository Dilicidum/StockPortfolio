using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

/// <summary>The untracked reads: one another module needs, one the dashboard needs.</summary>
internal sealed class HoldingQueries(PortfolioDbContext context) : IUserHoldsTicker, IDashboardHoldingReader
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<HoldingRow>> GetVisibleHoldingsAsync(Guid userId, CancellationToken ct)
    {
        // AveragePrice's MEMBERS, never AveragePrice itself: Money is rebuilt below, which also keeps
        // its ToUpperInvariant off the materialiser. No Include here, and a .Select would ignore one.
        var rows = await context.Holdings
            .AsNoTracking()
            .Where(h => h.UserId == userId && h.IsVisible)
            .OrderByDescending(h => h.CreatedAt)
            .Select(h => new Projected(
                h.Id,
                h.Ticker,
                h.Quantity,
                h.AveragePrice.Amount,
                h.AveragePrice.Currency))
            .ToListAsync(ct);

        return
        [
            .. rows.Select(row => new HoldingRow(
                row.Id.Value,
                row.Ticker.Value,
                row.Quantity,
                new Money(row.Amount, row.Currency))),
        ];
    }

    /// <summary>What the query materialises: converter-backed ids and scalars, no complex type.</summary>
    private sealed record Projected(
        HoldingId Id,
        Ticker Ticker,
        decimal Quantity,
        decimal Amount,
        string Currency);
}
