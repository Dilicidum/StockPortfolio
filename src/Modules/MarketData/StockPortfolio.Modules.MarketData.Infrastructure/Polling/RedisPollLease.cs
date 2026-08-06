using System.Globalization;

using Microsoft.Extensions.Logging;

using StackExchange.Redis;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Polling;

/// <summary>Two keys, not one: a minute claim says nothing about a cycle that ran past the end of its minute.</summary>
internal sealed partial class RedisPollLease(
    IConnectionMultiplexer multiplexer,
    PollingOptions options,
    ILogger<RedisPollLease> logger) : IPollLease
{
    private const string ClaimPrefix = "marketdata:claim:";

    private const string InFlightKey = "marketdata:cycle-inflight";

    /// <summary>Only presence is ever read, so the value is a placeholder rather than data.</summary>
    private const string Held = "1";

    public async Task<bool> TryAcquireAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var database = multiplexer.GetDatabase();

            // The claim first. Losing it means another replica already owns this minute, and the in-flight
            // flag must then not be touched at all — releasing one this replica does not hold would let two
            // cycles overlap, which is the single thing the second key exists to prevent.
            var claimed = await database.StringSetAsync(
                ClaimKey(now, options.Interval), Held, options.Interval * 2, false, When.NotExists);

            if (!claimed)
            {
                return false;
            }

            // Not keyed to the minute: this is what a cycle still running from an earlier minute holds, and
            // a minute key can say nothing about a neighbouring minute.
            return await database.StringSetAsync(
                InFlightKey, Held, options.Interval * 5, false, When.NotExists);
        }
        catch (RedisException ex)
        {
            LogAcquireFailed(logger, ex);

            // Skip the cycle rather than run it: with Redis unreachable there is nowhere to put a sample.
            return false;
        }
    }

    public async Task ReleaseAsync(CancellationToken ct)
    {
        try
        {
            await multiplexer.GetDatabase().KeyDeleteAsync(InFlightKey);
        }
        catch (RedisException ex)
        {
            LogReleaseFailed(logger, ex);
        }
    }

    /// <summary>The cycle this instant belongs to, counted from the epoch so every replica agrees.</summary>
    internal static string ClaimKey(DateTimeOffset now, TimeSpan interval)
    {
        // Keyed to the INTERVAL, not to the calendar minute. The key's lifetime is interval * 2, so a
        // minute-named key only survives its own minute while the interval is at least half a minute -
        // set the interval to 15s and the claim expires inside the minute it names, letting a second
        // replica claim that same minute. Deriving the name from the same number as the lifetime is what
        // keeps "one winner per cycle" true at every interval rather than only at the default one.
        var seconds = Math.Max(1L, (long)interval.TotalSeconds);

        return ClaimPrefix
            + (now.ToUnixTimeSeconds() / seconds).ToString(CultureInfo.InvariantCulture);
    }

    [LoggerMessage(
        EventId = 5140,
        Level = LogLevel.Warning,
        Message = "Redis would not grant a poll lease; this cycle is skipped and the next one retries")]
    private static partial void LogAcquireFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5141,
        Level = LogLevel.Warning,
        Message = "Redis would not release the in-flight flag; polling resumes when it expires")]
    private static partial void LogReleaseFailed(ILogger logger, Exception exception);
}
