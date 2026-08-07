using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Prices;

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
            if (Ticker.TryParse(candidate) is { } ticker)
            {
                requested.Add(ticker);
            }
        }

        if (requested.Count == 0)
        {
            return prices;
        }

        var apiKeyOverride = byokOptions.Enabled ? await keyReader.ReadPlaintextAsync(userId, ct) : null;

        var fetched = await provider.GetQuotesAsync(requested, apiKeyOverride, ct);

        if (apiKeyOverride is not null && fetched.Count == 0)
        {
            await MarkKeyIfRejectedAsync(userId, apiKeyOverride, ct);
        }

        await store.WriteAsync(fetched, ct);

        foreach (var quote in fetched)
        {
            prices[quote.Ticker.Value] =
                new QuotedPrice(quote.Ticker.Value, quote.Price, quote.ObservedAt, IsLastKnown: false);
        }

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

    private async Task MarkKeyIfRejectedAsync(Guid userId, string apiKeyOverride, CancellationToken ct)
    {
        if (await provider.VerifyKeyAsync(apiKeyOverride, ct) == KeyVerdict.Rejected)
        {
            await keyRepository.MarkRejectedAsync(userId, ct);
        }
    }
}
