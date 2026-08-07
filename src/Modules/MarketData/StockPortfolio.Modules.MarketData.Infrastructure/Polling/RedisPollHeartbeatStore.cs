using System.Globalization;

using Microsoft.Extensions.Logging;

using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Polling;

internal sealed partial class RedisPollHeartbeatStore(
    IConnectionMultiplexer multiplexer,
    ILogger<RedisPollHeartbeatStore> logger) : IPollHeartbeatStore
{
    private const string Key = "marketdata:poll:last";

    private const int Fields = 3;

    public async Task WriteAsync(PollHeartbeat heartbeat, CancellationToken ct)
    {
        try
        {
            await multiplexer.GetDatabase().StringSetAsync(Key, Encode(heartbeat));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogWriteFailed(logger, ex);
        }
    }

    public async Task<PollHeartbeat?> ReadAsync(CancellationToken ct)
    {
        try
        {
            return TryDecode(await multiplexer.GetDatabase().StringGetAsync(Key), out var heartbeat)
                ? heartbeat
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogReadFailed(logger, ex);

            return null;
        }
    }

    internal static string Encode(PollHeartbeat heartbeat) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{heartbeat.At.ToUnixTimeMilliseconds()}:{heartbeat.TickersTargeted}:{heartbeat.TickersStored}");

    internal static bool TryDecode(string? encoded, out PollHeartbeat heartbeat)
    {
        heartbeat = default;

        var parts = encoded?.Split(':');

        if (parts is not { Length: Fields }
            || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var epochMs)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var targeted)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var stored))
        {
            return false;
        }

        heartbeat = new PollHeartbeat(DateTimeOffset.FromUnixTimeMilliseconds(epochMs), targeted, stored);

        return true;
    }

    [LoggerMessage(
        EventId = 5144,
        Level = LogLevel.Warning,
        Message = "Redis would not take the poll heartbeat; the cycle itself succeeded and health reads stale")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 5145,
        Level = LogLevel.Warning,
        Message = "Redis would not return the poll heartbeat; the feed reports as though no cycle has run")]
    private static partial void LogReadFailed(ILogger logger, Exception exception);
}
