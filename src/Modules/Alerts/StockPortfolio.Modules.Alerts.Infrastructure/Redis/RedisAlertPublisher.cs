using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using StackExchange.Redis;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Redis;

/// <summary>Both halves of the fan-out, on one multiplexer: publish here, deliver on whichever replica listens.</summary>
internal sealed partial class RedisAlertPublisher(
    IConnectionMultiplexer multiplexer,
    ILogger<RedisAlertPublisher> logger) : IAlertPublisher, IAlertStreamSubscriber
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

    /// <summary>Subscribes this replica to one user's channel. Without it an alert produced on another
    /// replica reaches nobody, and only for the users whose stream happens to be held elsewhere.</summary>
    public async Task<IAsyncDisposable> SubscribeAsync(
        Guid userId,
        ChannelWriter<AlertNotification> writer,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var channel = ChannelFor(userId);
        var queue = await multiplexer.GetSubscriber().SubscribeAsync(channel);

        // OnMessage rather than a callback per message: it serialises delivery onto one worker, so the
        // frames a browser receives are in the order Redis produced them.
        queue.OnMessage(message =>
        {
            var payload = (string?)message.Message;

            if (payload is null)
            {
                return;
            }

            try
            {
                var notification = JsonSerializer.Deserialize<AlertNotification>(payload, Wire);

                if (notification is not null)
                {
                    // Dropped rather than awaited: the channel is unbounded, so a false result means
                    // the reader has already gone, which is the ordinary end of every stream.
                    _ = writer.TryWrite(notification);
                }
            }
            catch (JsonException ex)
            {
                LogUnreadablePayload(logger, ex, userId);
            }
        });

        return new Subscription(queue);
    }

    /// <summary>One channel per user, so a replica subscribes to exactly the traffic it can deliver.</summary>
    internal static RedisChannel ChannelFor(Guid userId) => RedisChannel.Literal(
        string.Create(CultureInfo.InvariantCulture, $"{ChannelPrefix}{userId:D}"));

    [LoggerMessage(
        EventId = 5330,
        Level = LogLevel.Warning,
        Message = "An alert payload for {UserId} could not be read off the channel; that one push is lost "
            + "and the history read still carries the row")]
    private static partial void LogUnreadablePayload(ILogger logger, Exception exception, Guid userId);

    /// <summary>Unsubscribing is what the stream's cleanup does; the queue is per connection.</summary>
    private sealed class Subscription(ChannelMessageQueue queue) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await queue.UnsubscribeAsync();
    }
}
