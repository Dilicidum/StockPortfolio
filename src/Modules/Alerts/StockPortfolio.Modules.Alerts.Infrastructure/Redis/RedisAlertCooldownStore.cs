using System.Globalization;

using Microsoft.Extensions.Logging;

using StackExchange.Redis;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Redis;

internal sealed partial class RedisAlertCooldownStore(
    IConnectionMultiplexer multiplexer,
    ILogger<RedisAlertCooldownStore> logger) : IAlertCooldownStore
{
    private const string KeyPrefix = "alerts:cooldown:";

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
            // ONE round trip, with When.NotExists deciding: read-then-write lets two replicas both find it absent and send two alerts for one breach.
            return await multiplexer.GetDatabase()
                .StringSetAsync(key, Held, cooldown, keepTtl: false, When.NotExists);
        }
        catch (RedisException ex)
        {
            // Suppressed rather than resent: with no way to tell whether this alert already went out, retrying turns a Redis outage into duplicates.
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
