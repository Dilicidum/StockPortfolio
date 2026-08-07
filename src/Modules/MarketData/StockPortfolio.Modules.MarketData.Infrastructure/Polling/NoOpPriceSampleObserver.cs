using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Polling;

internal sealed class NoOpPriceSampleObserver : IPriceSampleObserver
{
    public Task OnSampleStoredAsync(string ticker, CancellationToken ct) => Task.CompletedTask;
}
