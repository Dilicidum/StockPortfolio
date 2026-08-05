using System.Globalization;

using Microsoft.Extensions.Logging;

using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Prices;

/// <summary>The dashboard's only fallback when the provider is down. Never trimmed, never allowed to throw out.</summary>
internal sealed partial class RedisLastKnownPriceStore(
    IConnectionMultiplexer multiplexer,
    ILogger<RedisLastKnownPriceStore> logger) : ILastKnownPriceStore
{
    private const string KeyPrefix = "marketdata:last:";

    public async Task<IReadOnlyDictionary<Ticker, LastPrice>> ReadAsync(
        IReadOnlyCollection<Ticker> tickers,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tickers);

        var prices = new Dictionary<Ticker, LastPrice>();

        if (tickers.Count == 0)
        {
            return prices;
        }

        var ordered = tickers.ToArray();

        try
        {
            // One MGET for the whole missing set: a string type is what makes that one round trip.
            var values = await multiplexer.GetDatabase()
                .StringGetAsync([.. ordered.Select(ticker => (RedisKey)(KeyPrefix + ticker.Value))]);

            for (var index = 0; index < ordered.Length; index++)
            {
                if (TryDecode(values[index], out var price))
                {
                    prices[ordered[index]] = price;
                }
            }
        }
        catch (RedisException ex)
        {
            LogReadFailed(logger, ex, ordered.Length);
        }

        return prices;
    }

    public async Task WriteAsync(IReadOnlyCollection<Quote> quotes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(quotes);

        if (quotes.Count == 0)
        {
            return;
        }

        try
        {
            var database = multiplexer.GetDatabase();

            // Awaited, not FireAndForget: that flag only returns the default value early, it does not
            // stop connection and backlog-timeout exceptions surfacing here - and awaiting is what makes
            // `redis-cli GET marketdata:last:AAPL` non-racy right after a dashboard load.
            await Task.WhenAll(quotes.Select(quote =>
                database.StringSetAsync(KeyPrefix + quote.Ticker.Value, Encode(quote.Price, quote.ObservedAt))));
        }
        catch (RedisException ex)
        {
            LogWriteFailed(logger, ex, quotes.Count);
        }
    }

    /// <summary>InvariantCulture explicitly: a comma separator here would corrupt every stored price silently.</summary>
    internal static string Encode(decimal price, DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture, $"{price}:{at.ToUnixTimeMilliseconds()}");

    /// <summary>A corrupt entry is "no last-known price", never a throw — the provider is already down.</summary>
    internal static bool TryDecode(string? encoded, out LastPrice price)
    {
        price = default;

        if (encoded is null)
        {
            return false;
        }

        var separator = encoded.LastIndexOf(':');

        if (separator <= 0
            || !decimal.TryParse(
                encoded.AsSpan(0, separator), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            || !long.TryParse(
                encoded.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochMs))
        {
            return false;
        }

        price = new LastPrice(amount, DateTimeOffset.FromUnixTimeMilliseconds(epochMs));

        return true;
    }

    [LoggerMessage(
        EventId = 5110,
        Level = LogLevel.Warning,
        Message = "Redis read of {Count} last-known prices failed; those positions render without a price")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, int count);

    [LoggerMessage(
        EventId = 5111,
        Level = LogLevel.Warning,
        Message = "Redis write of {Count} last-known prices failed; the request itself is unaffected")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, int count);
}
