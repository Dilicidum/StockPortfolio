using StockPortfolio.Modules.Alerts.Contracts;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Api.Adapters;

/// <summary>Hands each fresh sample to Alerts, which is the only reason the poller exists.</summary>
internal sealed class AlertsPriceSampleObserver(IAlertEvaluator evaluator) : IPriceSampleObserver
{
    /// <inheritdoc/>
    public Task OnSampleStoredAsync(string ticker, CancellationToken ct) =>
        evaluator.EvaluateAsync(ticker, ct);
}
