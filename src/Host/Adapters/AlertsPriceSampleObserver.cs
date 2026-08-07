using StockPortfolio.Modules.Alerts.Contracts;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Host.Adapters;

internal sealed class AlertsPriceSampleObserver(IAlertEvaluator evaluator) : IPriceSampleObserver
{
    public Task OnSampleStoredAsync(string ticker, CancellationToken ct) =>
        evaluator.EvaluateAsync(ticker, ct);
}
