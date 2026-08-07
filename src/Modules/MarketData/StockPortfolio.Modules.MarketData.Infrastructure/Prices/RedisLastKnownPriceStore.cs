using System.Globalization;

using Microsoft.Extensions.Logging;

using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Prices;

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

            await Task.WhenAll(quotes.Select(quote =>
                database.StringSetAsync(KeyPrefix + quote.Ticker.Value, Encode(quote.Price, quote.ObservedAt))));
        }
        catch (RedisException ex)
        {
            LogWriteFailed(logger, ex, quotes.Count);
        }
    }

    internal static string Encode(decimal price, DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture, $"{price}:{at.ToUnixTimeMilliseconds()}");

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
