using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Microsoft.AspNetCore.Http;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Modules.Alerts.Api;

/// <summary>The stream body: one named alert event per push, and a named ping the rest of the time.</summary>
public static class AlertFeed
{
    /// <summary>The event name the browser listens for. An unnamed frame arrives as "message" and is lost.</summary>
    public const string AlertEventName = "alert";

    /// <summary>The heartbeat's name. SseFormatter has no comment API, so the beat is a real event.</summary>
    public const string PingEventName = "ping";

    /// <summary>20s against the platform's 4-minute idle close, where 4 minutes is the default AND the floor.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    /// <summary>The stream as a result. Kept here so the overload choice below is assertable.</summary>
    public static IResult Result(
        Guid userId,
        IAlertStreamSubscriber subscriber,
        TimeProvider clock,
        CancellationToken ct) =>
        // NO eventType argument. ServerSentEvents has two overloads and IAsyncEnumerable<SseItem<T>>
        // satisfies both, so passing one selects the IAsyncEnumerable<T> overload with T = SseItem<object>:
        // every frame then arrives unnamed, carrying the wrapper as its data, and a client listening for
        // "alert" sees nothing at all.
        TypedResults.ServerSentEvents(StreamAsync(userId, subscriber, clock, ct));

    /// <summary>Drains one queue of ready-made frames; alerts and beats are both written into it.</summary>
    public static async IAsyncEnumerable<SseItem<object>> StreamAsync(
        Guid userId,
        IAlertStreamSubscriber subscriber,
        TimeProvider clock,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Unbounded: a burst of alerts for one user is a handful of small records, and a bounded
        // writer would have to choose between blocking the Redis worker and dropping an alert.
        var frames = Channel.CreateUnbounded<SseItem<object>>();

        // Written before the subscription exists, so it is first in the queue and cannot race an alert.
        // Nothing reaches the socket until a frame does, so a stream whose first frame is twenty seconds
        // away leaves the response headers unsent and looks to a proxy like a connection still opening.
        frames.Writer.TryWrite(Ping(clock));

        await using var subscription = await subscriber.SubscribeAsync(
            userId, new AlertFrameWriter(frames.Writer), ct);

        // Cancellation closes the queue rather than tearing through the read below, so the browser
        // closing its tab ends this enumerable normally instead of throwing out of it.
        using var cancellation = ct.Register(() => frames.Writer.TryComplete());

        using var heartbeat = clock.CreateTimer(
            _ => frames.Writer.TryWrite(Ping(clock)), null, HeartbeatInterval, HeartbeatInterval);

        await foreach (var frame in frames.Reader.ReadAllAsync(CancellationToken.None))
        {
            yield return frame;
        }
    }

    /// <summary>The heartbeat payload. A named event with no listener is dropped before it reaches code.</summary>
    public sealed record Heartbeat(DateTimeOffset At);

    private static SseItem<object> Ping(TimeProvider clock) =>
        new(new Heartbeat(clock.GetUtcNow()), PingEventName);

    /// <summary>Names each alert as it is written, so the subscriber and the beat share one queue.</summary>
    private sealed class AlertFrameWriter(ChannelWriter<SseItem<object>> frames)
        : ChannelWriter<AlertNotification>
    {
        public override bool TryWrite(AlertNotification item) =>
            frames.TryWrite(new SseItem<object>(item, AlertEventName));

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken ct = default) =>
            frames.WaitToWriteAsync(ct);
    }
}
