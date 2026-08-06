using Microsoft.EntityFrameworkCore;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence;

internal sealed class AlertSettingRepository(AlertsDbContext context) : IAlertSettingRepository
{
    // No AsNoTracking on the find: ChangeTracker.Entries<T>() only sees tracked entities, so an
    // untracked read means Adjust changes an object nobody saves, with no error at all.
    public async Task<AlertSetting?> FindAsync(Guid userId, string ticker, CancellationToken ct)
    {
        // Wrapped, not created: the caller has already canonicalised, exactly as the converter's read
        // path has. A lookup that canonicalises differently from what was stored simply misses.
        var symbol = new Ticker(ticker);

        return await context.AlertSettings
            .FirstOrDefaultAsync(setting => setting.UserId == userId && setting.Ticker == symbol, ct);
    }

    public async Task<IReadOnlyList<AlertSetting>> ListForUserAsync(Guid userId, CancellationToken ct)
        => await context.AlertSettings
            .Where(setting => setting.UserId == userId)
            .OrderBy(setting => setting.Ticker)
            .ToListAsync(ct);

    // Filtered in SQL on the ticker as well as the flag: an evaluation is told about one symbol, and
    // reading every enabled setting in the database to keep a handful of them is the same query with
    // the work moved to the wrong machine.
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
        // Distinct in SQL, unwrapped afterwards: EF cannot see inside a value-converted type, so
        // .Select(ticker => ticker.Value) in the query would not translate.
        var tickers = await context.AlertSettings
            .Where(setting => setting.Enabled)
            .Select(setting => setting.Ticker)
            .Distinct()
            .ToListAsync(ct);

        return [.. tickers.Select(ticker => ticker.Value).Order(StringComparer.Ordinal)];
    }

    // One method for insert and update: a tracked setting is already in the context, and an untracked
    // one is new. Add on an entity EF is tracking would throw, so the state decides, not the caller.
    public async Task SaveAsync(AlertSetting setting, CancellationToken ct)
    {
        if (context.Entry(setting).State == EntityState.Detached)
        {
            context.AlertSettings.Add(setting);
        }

        await context.SaveChangesAsync(ct);
    }
}
