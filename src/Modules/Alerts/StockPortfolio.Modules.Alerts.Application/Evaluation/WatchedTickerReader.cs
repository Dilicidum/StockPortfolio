using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Contracts;

namespace StockPortfolio.Modules.Alerts.Application.Evaluation;

public sealed class WatchedTickerReader(IAlertSettingRepository settings) : IWatchedTickerReader
{
    public Task<IReadOnlyList<string>> GetWatchedTickersAsync(CancellationToken ct) =>
        settings.ListEnabledTickersAsync(ct);
}
