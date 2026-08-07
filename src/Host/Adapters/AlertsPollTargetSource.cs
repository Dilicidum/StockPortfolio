using StockPortfolio.Modules.Alerts.Contracts;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Host.Adapters;

internal sealed class AlertsPollTargetSource(IWatchedTickerReader reader) : IPollTargetSource
{
    public Task<IReadOnlyList<string>> GetPollTargetsAsync(CancellationToken ct) =>
        reader.GetWatchedTickersAsync(ct);
}
