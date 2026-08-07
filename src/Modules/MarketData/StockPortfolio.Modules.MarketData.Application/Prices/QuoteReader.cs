using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Prices;

public sealed partial class QuoteReader(
    IQuoteProvider provider,
    ILastKnownPriceStore store,
    IUserProviderKeyReader keyReader,
    IUserProviderKeyRepository keyRepository,
    ByokOptions byokOptions,
    TimeProvider clock,
    ILogger<QuoteReader> logger) : IQuoteReader
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

        var apiKeyOverride = await ReadOwnKeyAsync(userId, ct);

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

    /// <summary>A corrupt key ring or a database blip must cost this user their own key, never the whole dashboard.</summary>
    private async Task<string?> ReadOwnKeyAsync(Guid userId, CancellationToken ct)
    {
        if (!byokOptions.Enabled)
        {
            return null;
        }

        try
        {
            return await keyReader.ReadPlaintextAsync(userId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogOwnKeyUnreadable(logger, ex);

            return null;
        }
    }

    private async Task MarkKeyIfRejectedAsync(Guid userId, string apiKeyOverride, CancellationToken ct)
    {
        if (await provider.VerifyKeyAsync(apiKeyOverride, ct) == KeyVerdict.Rejected)
        {
            await keyRepository.MarkRejectedAsync(userId, ct);
        }
    }

    [LoggerMessage(
        EventId = 5150,
        Level = LogLevel.Warning,
        Message = "This user's own provider key could not be read; the fetch falls back to the application's key")]
    private static partial void LogOwnKeyUnreadable(ILogger logger, Exception exception);
}
