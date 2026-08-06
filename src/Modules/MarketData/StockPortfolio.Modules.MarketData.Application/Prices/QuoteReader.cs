using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Prices;

/// <summary>Provider first, last-known second. The dashboard's whole path to a price.</summary>
public sealed class QuoteReader(
    IQuoteProvider provider,
    ILastKnownPriceStore store,
    TimeProvider clock) : IQuoteReader
{
    public async Task<IReadOnlyDictionary<string, QuotedPrice>> GetCurrentPricesAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tickers);

        var prices = new Dictionary<string, QuotedPrice>(StringComparer.Ordinal);
        var requested = new HashSet<Ticker>();

        foreach (var candidate in tickers)
        {
            if (Ticker.Create(candidate).TryPickT0(out var ticker, out _))
            {
                requested.Add(ticker);
            }
        }

        if (requested.Count == 0)
        {
            return prices;
        }

        var fetched = await provider.GetQuotesAsync(requested, ct);

        // The write lives here and in the poller, never in a provider: with no API key the fake is the only
        // provider, so a write inside FinnhubQuoteProvider would leave marketdata:last:* empty on the whole
        // P0 compose path.
        await store.WriteAsync(fetched, ct);

        foreach (var quote in fetched)
        {
            prices[quote.Ticker.Value] =
                new QuotedPrice(quote.Ticker.Value, quote.Price, quote.ObservedAt, IsLastKnown: false);
        }

        // A set difference, not one try/catch round the whole call: three tickers failing must not discard
        // the seventeen that succeeded and replace them with stale values.
        var missing = requested.Where(ticker => !prices.ContainsKey(ticker.Value)).ToArray();

        if (missing.Length == 0)
        {
            return prices;
        }

        var now = clock.GetUtcNow();

        foreach (var (ticker, last) in await store.ReadAsync(missing, ct))
        {
            if (LastKnownPrice.IsWorthShowing(last, now))
            {
                prices[ticker.Value] =
                    new QuotedPrice(ticker.Value, last.Price, last.ObservedAt, IsLastKnown: true);
            }
        }

        return prices;
    }
}
