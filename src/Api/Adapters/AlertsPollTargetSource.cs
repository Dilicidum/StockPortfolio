using StockPortfolio.Modules.Alerts.Contracts;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Api.Adapters;

/// <summary>Answers MarketData's "which tickers do I poll" from Alerts, so neither module names the other.</summary>
internal sealed class AlertsPollTargetSource(IWatchedTickerReader reader) : IPollTargetSource
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetPollTargetsAsync(CancellationToken ct) =>
        reader.GetWatchedTickersAsync(ct);
}
