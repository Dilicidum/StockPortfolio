using System.Globalization;

using Microsoft.Extensions.Logging;

using StackExchange.Redis;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Redis;

/// <summary>One key per user, ticker and direction, and it expires on its own — no cleanup anywhere.</summary>
internal sealed partial class RedisAlertCooldownStore(
    IConnectionMultiplexer multiplexer,
    ILogger<RedisAlertCooldownStore> logger) : IAlertCooldownStore
{
    private const string KeyPrefix = "alerts:cooldown:";

    /// <summary>The value is never read; only whether the key exists means anything.</summary>
    private const string Held = "1";

    public async Task<bool> TryStartAsync(
        Guid userId,
        string ticker,
        AlertDirection direction,
        TimeSpan cooldown,
        CancellationToken ct)
    {
        var key = string.Create(
            CultureInfo.InvariantCulture,
            $"{KeyPrefix}{userId:D}:{ticker}:{direction}");

        try
        {
            // ONE round trip, with When.NotExists doing the deciding. Reading the key and then writing
            // it lets two replicas both find it absent in the same millisecond and send two alerts for
            // one breach - which is the exact failure the cooldown exists to prevent.
            return await multiplexer.GetDatabase()
                .StringSetAsync(key, Held, cooldown, keepTtl: false, When.NotExists);
        }
        catch (RedisException ex)
        {
            // Silence, not noise: with no way to tell whether this alert was already sent, sending it
            // again would turn a Redis outage into a burst of duplicates in somebody's panel.
            LogCooldownUnavailable(logger, ex, ticker);

            return false;
        }
    }

    [LoggerMessage(
        EventId = 5320,
        Level = LogLevel.Warning,
        Message = "The cooldown for {Ticker} could not be claimed in Redis, so the alert is suppressed "
            + "rather than risked as a duplicate")]
    private static partial void LogCooldownUnavailable(ILogger logger, Exception exception, string ticker);
}
