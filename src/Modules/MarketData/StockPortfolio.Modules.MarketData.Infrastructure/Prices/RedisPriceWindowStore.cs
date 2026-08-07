using System.Globalization;

using Microsoft.Extensions.Logging;

using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Prices;

/// <summary>The trimmed alert series. Separate from the last-known key because retention must not reach it.</summary>
internal sealed partial class RedisPriceWindowStore(
    IConnectionMultiplexer multiplexer,
    ILogger<RedisPriceWindowStore> logger) : IPriceWindowStore
{
    private const string KeyPrefix = "marketdata:prices:";

    public async Task AppendAsync(
        string ticker,
        decimal price,
        DateTimeOffset at,
        TimeSpan retention,
        CancellationToken ct)
    {
        try
        {
            var database = multiplexer.GetDatabase();
            var key = (RedisKey)(KeyPrefix + ticker);
            var cutoff = (double)(at - retention).ToUnixTimeMilliseconds();

            var batch = database.CreateBatch();

            var add = batch.SortedSetAddAsync(key, Encode(price, at), at.ToUnixTimeMilliseconds());
            var trim = batch.SortedSetRemoveRangeByScoreAsync(
                key, double.NegativeInfinity, cutoff, Exclude.Stop);

            var expire = batch.KeyExpireAsync(key, retention * 2);

            batch.Execute();

            await Task.WhenAll(add, trim, expire);
        }
        catch (RedisException ex)
        {
            LogAppendFailed(logger, ex, ticker);
        }
    }

    public async Task<IReadOnlyList<(DateTimeOffset At, decimal Price)>> ReadAsync(
        string ticker,
        DateTimeOffset since,
        CancellationToken ct)
    {
        try
        {
            var members = await multiplexer.GetDatabase().SortedSetRangeByScoreAsync(
                KeyPrefix + ticker,
                since.ToUnixTimeMilliseconds(),
                double.PositiveInfinity,
                Exclude.None,
                Order.Ascending);

            var samples = new List<(DateTimeOffset At, decimal Price)>(members.Length);

            foreach (var member in members)
            {
                if (TryDecode(member, out var sample))
                {
                    samples.Add(sample);
                }
            }

            return samples;
        }
        catch (RedisException ex)
        {
            LogReadFailed(logger, ex, ticker);

            return [];
        }
    }

    /// <summary>Stamp first so one price printed twice is two members; a bare price would erase the earlier one.</summary>
    internal static string Encode(decimal price, DateTimeOffset at) =>
        string.Create(CultureInfo.InvariantCulture, $"{at.ToUnixTimeMilliseconds()}:{price}");

    internal static bool TryDecode(string? encoded, out (DateTimeOffset At, decimal Price) sample)
    {
        sample = default;

        if (encoded is null)
        {
            return false;
        }

        var separator = encoded.IndexOf(':', StringComparison.Ordinal);

        if (separator <= 0
            || !long.TryParse(
                encoded.AsSpan(0, separator), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochMs)
            || !decimal.TryParse(
                encoded.AsSpan(separator + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var price))
        {
            return false;
        }

        sample = (DateTimeOffset.FromUnixTimeMilliseconds(epochMs), price);

        return true;
    }

    [LoggerMessage(
        EventId = 5130,
        Level = LogLevel.Warning,
        Message = "Redis append to the price window for {Ticker} failed; that cycle's sample is lost")]
    private static partial void LogAppendFailed(ILogger logger, Exception exception, string ticker);

    [LoggerMessage(
        EventId = 5131,
        Level = LogLevel.Warning,
        Message = "Redis read of the price window for {Ticker} failed; alerts on it are suppressed this cycle")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, string ticker);
}
