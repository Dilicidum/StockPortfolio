using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Prices;

/// <summary>Provider first, last-known second. The dashboard's whole path to a price.</summary>
public sealed class QuoteReader(
    IQuoteProvider provider,
    ILastKnownPriceStore store,
    IUserProviderKeyReader keyReader,
    IUserProviderKeyRepository keyRepository,
    ByokOptions byokOptions,
    TimeProvider clock) : IQuoteReader
{
    public async Task<IReadOnlyDictionary<string, QuotedPrice>> GetCurrentPricesAsync(
        Guid userId,
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

        // Resolved once per call, before the fan-out: one database read and one decrypt for a whole
        // dashboard load, never one per ticker. Skipped entirely while the switch is off, so a stored key
        // is never read, decrypted or sent to the provider once BYOK is disabled - a saved key stays on
        // file, but stops being used.
        var apiKeyOverride = byokOptions.Enabled ? await keyReader.ReadPlaintextAsync(userId, ct) : null;

        var fetched = await provider.GetQuotesAsync(requested, apiKeyOverride, ct);

        if (apiKeyOverride is not null && fetched.Count == 0)
        {
            // Every fetch using this key came back empty. That is also what a provider outage looks
            // like, so ask the provider directly rather than guessing from the shape of the miss.
            await MarkKeyIfRejectedAsync(userId, apiKeyOverride, ct);
        }

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

    /// <summary>Confirms a rejection before recording one: an outage must degrade quietly, not brand the key bad.</summary>
    private async Task MarkKeyIfRejectedAsync(Guid userId, string apiKeyOverride, CancellationToken ct)
    {
        if (await provider.VerifyKeyAsync(apiKeyOverride, ct) == KeyVerdict.Rejected)
        {
            await keyRepository.MarkRejectedAsync(userId, ct);
        }
    }
}
