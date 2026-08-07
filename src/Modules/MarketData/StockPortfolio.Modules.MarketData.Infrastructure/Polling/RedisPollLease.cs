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

    private const string Held = "1";

    public async Task<bool> TryAcquireAsync(DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var database = multiplexer.GetDatabase();

            // The claim first: a refused claim must leave the in-flight key untouched, or releasing it deletes the winner's and lets two cycles overlap.
            var claimed = await database.StringSetAsync(
                ClaimKey(now, options.Interval), Held, options.Interval * 2, false, When.NotExists);

            if (!claimed)
            {
                return false;
            }

            return await database.StringSetAsync(
                InFlightKey, Held, options.Interval * 5, false, When.NotExists);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAcquireFailed(logger, ex);

            return false;
        }
    }

    public async Task ReleaseAsync(CancellationToken ct)
    {
        try
        {
            await multiplexer.GetDatabase().KeyDeleteAsync(InFlightKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReleaseFailed(logger, ex);
        }
    }

    internal static string ClaimKey(DateTimeOffset now, TimeSpan interval)
    {
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
