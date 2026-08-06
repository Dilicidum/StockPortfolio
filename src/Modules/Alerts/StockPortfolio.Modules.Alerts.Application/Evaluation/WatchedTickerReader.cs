using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Contracts;

namespace StockPortfolio.Modules.Alerts.Application.Evaluation;

/// <summary>The poll list. With no thresholds anywhere it is empty, and then nothing is polled at all.</summary>
public sealed class WatchedTickerReader(IAlertSettingRepository settings) : IWatchedTickerReader
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetWatchedTickersAsync(CancellationToken ct) =>
        settings.ListEnabledTickersAsync(ct);
}
