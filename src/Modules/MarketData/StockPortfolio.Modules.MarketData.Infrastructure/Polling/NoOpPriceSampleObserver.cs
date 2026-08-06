using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Polling;

/// <summary>Nobody is listening. Registered by default so MarketData runs standalone with no stub.</summary>
internal sealed class NoOpPriceSampleObserver : IPriceSampleObserver
{
    public Task OnSampleStoredAsync(string ticker, CancellationToken ct) => Task.CompletedTask;
}
