namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

public interface IPriceSampleObserver
{
    Task OnSampleStoredAsync(string ticker, CancellationToken ct);
}
