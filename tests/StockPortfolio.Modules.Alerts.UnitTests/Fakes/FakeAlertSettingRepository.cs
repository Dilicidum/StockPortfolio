using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Tests.Fakes;

/// <summary>An in-memory alert_settings table. The first fake in the repository — see B4/B6.</summary>
internal sealed class FakeAlertSettingRepository : IAlertSettingRepository
{
    private readonly List<AlertSetting> _rows = [];

    /// <summary>Gets how many times a ticker's enabled settings were read.</summary>
    public int EnabledReadCount { get; private set; }

    /// <summary>Adds a row without going through the handler, so a test can arrange its own state.</summary>
    public FakeAlertSettingRepository With(AlertSetting setting)
    {
        _rows.Add(setting);

        return this;
    }

    public Task<AlertSetting?> FindAsync(Guid userId, string ticker, CancellationToken ct) =>
        Task.FromResult(_rows.Find(row =>
            row.UserId == userId && string.Equals(row.Ticker.Value, ticker, StringComparison.Ordinal)));

    public Task<IReadOnlyList<AlertSetting>> ListForUserAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<AlertSetting>>(
            [.. _rows.Where(row => row.UserId == userId).OrderBy(row => row.Ticker.Value, StringComparer.Ordinal)]);

    public Task<IReadOnlyList<AlertSetting>> ListEnabledForTickerAsync(string ticker, CancellationToken ct)
    {
        EnabledReadCount++;

        return Task.FromResult<IReadOnlyList<AlertSetting>>(
        [
            .. _rows
                .Where(row => row.Enabled && string.Equals(row.Ticker.Value, ticker, StringComparison.Ordinal))
                .OrderBy(row => row.UserId),
        ]);
    }

    public Task<IReadOnlyList<string>> ListEnabledTickersAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(
        [
            .. _rows
                .Where(row => row.Enabled)
                .Select(row => row.Ticker.Value)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ]);

    public Task SaveAsync(AlertSetting setting, CancellationToken ct)
    {
        if (!_rows.Contains(setting))
        {
            _rows.Add(setting);
        }

        return Task.CompletedTask;
    }
}
