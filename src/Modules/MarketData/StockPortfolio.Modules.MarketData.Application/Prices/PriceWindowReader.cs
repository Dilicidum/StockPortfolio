using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Prices;

/// <summary>Reduces a stored series to one window in a single pass; the shape fields are what the guards read.</summary>
public sealed class PriceWindowReader(IPriceWindowStore store, TimeProvider clock) : IPriceWindowReader
{
    public async Task<PriceWindow?> GetWindowAsync(string ticker, TimeSpan window, CancellationToken ct)
    {
        if (Ticker.TryParse(ticker) is not { } symbol
            || window <= TimeSpan.Zero)
        {
            return null;
        }

        var samples = await store.ReadAsync(symbol.Value, clock.GetUtcNow() - window, ct);

        if (samples.Count == 0)
        {
            // Absent, never a zero-filled window: a window of zeroes reads as a 100% crash to every rule.
            return null;
        }

        var low = samples[0].Price;
        var high = samples[0].Price;
        var largestGap = TimeSpan.Zero;

        for (var index = 1; index < samples.Count; index++)
        {
            var (at, price) = samples[index];

            low = Math.Min(low, price);
            high = Math.Max(high, price);

            var gap = at - samples[index - 1].At;

            if (gap > largestGap)
            {
                largestGap = gap;
            }
        }

        return new PriceWindow(
            symbol.Value,
            samples[^1].Price,
            samples[0].Price,
            low,
            high,
            samples[0].At,
            samples[^1].At,
            samples.Count,
            largestGap);
    }
}
