namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>Told once per ticker per cycle, after the sample is stored. Must never throw: a failed
/// observer must not stop the next ticker being sampled.</summary>
public interface IPriceSampleObserver
{
    Task OnSampleStoredAsync(string ticker, CancellationToken ct);
}
