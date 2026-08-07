using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence;

internal sealed class AlertSettingRepository(AlertsDbContext context) : IAlertSettingRepository
{
    public async Task<AlertSetting?> FindAsync(Guid userId, string ticker, CancellationToken ct)
    {
        var symbol = new Ticker(ticker);

        return await context.AlertSettings
            .FirstOrDefaultAsync(setting => setting.UserId == userId && setting.Ticker == symbol, ct);
    }

    public async Task<IReadOnlyList<AlertSetting>> ListForUserAsync(Guid userId, CancellationToken ct)
        => await context.AlertSettings
            .Where(setting => setting.UserId == userId)
            .OrderBy(setting => setting.Ticker)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AlertSetting>> ListEnabledForTickerAsync(string ticker, CancellationToken ct)
    {
        var symbol = new Ticker(ticker);

        return await context.AlertSettings
            .AsNoTracking()
            .Where(setting => setting.Enabled && setting.Ticker == symbol)
            .OrderBy(setting => setting.UserId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListEnabledTickersAsync(CancellationToken ct)
    {
        var tickers = await context.AlertSettings
            .Where(setting => setting.Enabled)
            .Select(setting => setting.Ticker)
            .Distinct()
            .ToListAsync(ct);

        return [.. tickers.Select(ticker => ticker.Value).Order(StringComparer.Ordinal)];
    }

    public async Task SaveAsync(AlertSetting setting, CancellationToken ct)
    {
        if (context.Entry(setting).State == EntityState.Detached)
        {
            context.AlertSettings.Add(setting);
        }

        await context.SaveChangesAsync(ct);
    }
}
