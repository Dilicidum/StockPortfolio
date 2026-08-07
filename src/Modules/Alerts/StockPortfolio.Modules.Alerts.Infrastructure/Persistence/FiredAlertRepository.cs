using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence;

internal sealed class FiredAlertRepository(AlertsDbContext context) : IFiredAlertRepository
{
    public async Task AddAsync(FiredAlert alert, CancellationToken ct)
    {
        context.FiredAlerts.Add(alert);
        await context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FiredAlertRow>> ListRecentAsync(
        Guid userId,
        int limit,
        CancellationToken ct)
    {
        var rows = await context.FiredAlerts
            .AsNoTracking()
            .Where(alert => alert.UserId == userId)
            .OrderByDescending(alert => alert.FiredAt)
            .Take(limit)
            .Select(alert => new Projected(
                alert.Id,
                alert.Ticker,
                alert.Direction,
                alert.ChangePercent,
                alert.EndpointPercent,
                alert.TriggerPrice.Amount,
                alert.TriggerPrice.Currency,
                alert.ReferencePrice.Amount,
                alert.ReferencePrice.Currency,
                alert.FiredAt,
                alert.IsSimulated))
            .ToListAsync(ct);

        return
        [
            .. rows.Select(row => new FiredAlertRow(
                row.Id.Value,
                row.Ticker.Value,
                row.Direction,
                row.ChangePercent,
                row.EndpointPercent,
                new Money(row.TriggerAmount, row.TriggerCurrency),
                new Money(row.ReferenceAmount, row.ReferenceCurrency),
                row.FiredAt,
                row.IsSimulated)),
        ];
    }

    private sealed record Projected(
        FiredAlertId Id,
        Ticker Ticker,
        AlertDirection Direction,
        decimal ChangePercent,
        decimal EndpointPercent,
        decimal TriggerAmount,
        string TriggerCurrency,
        decimal ReferenceAmount,
        string ReferenceCurrency,
        DateTimeOffset FiredAt,
        bool IsSimulated);
}
