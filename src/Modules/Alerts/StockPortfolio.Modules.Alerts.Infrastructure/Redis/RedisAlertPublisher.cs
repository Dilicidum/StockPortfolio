using System.Globalization;
using System.Text.Json;

using StackExchange.Redis;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Redis;

/// <summary>Publishes an alert to the one user it belongs to, across every replica.</summary>
internal sealed class RedisAlertPublisher(IConnectionMultiplexer multiplexer) : IAlertPublisher
{
    private const string ChannelPrefix = "alerts:user:";

    /// <summary>The channel's own shape, unrelated to the HTTP one: nothing outside this module reads it.</summary>
    internal static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    /// <summary>Pub/sub, not a list: an alert nobody is connected for is already saved and is not queued.</summary>
    public async Task PublishAsync(AlertNotification notification, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(notification);

        await multiplexer.GetSubscriber().PublishAsync(
            ChannelFor(notification.UserId),
            JsonSerializer.Serialize(notification, Wire));
    }

    /// <summary>One channel per user, so a replica subscribes to exactly the traffic it can deliver.</summary>
    internal static RedisChannel ChannelFor(Guid userId) => RedisChannel.Literal(
        string.Create(CultureInfo.InvariantCulture, $"{ChannelPrefix}{userId:D}"));
}
