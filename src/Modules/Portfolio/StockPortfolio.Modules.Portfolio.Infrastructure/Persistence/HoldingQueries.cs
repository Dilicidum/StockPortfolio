using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

internal sealed class HoldingQueries(PortfolioDbContext context) : IUserHoldsTicker, IDashboardHoldingReader
{
    public Task<bool> HoldsAsync(Guid userId, string ticker, CancellationToken ct) =>
        Ticker.Create(ticker).Match(
            parsed => HoldsAsync(userId, parsed, ct),
            badTicker => Task.FromResult(false));

    // No visibility filter: a hidden position is still held.
    private Task<bool> HoldsAsync(Guid userId, Ticker parsed, CancellationToken ct) =>
        context.Holdings
            .AsNoTracking()
            .AnyAsync(h => h.UserId == userId && h.Ticker == parsed, ct);

    public async Task<IReadOnlyList<HoldingRow>> GetVisibleHoldingsAsync(Guid userId, CancellationToken ct)
    {
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

    private sealed record Projected(
        HoldingId Id,
        Ticker Ticker,
        decimal Quantity,
        decimal Amount,
        string Currency);
}
