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

    /// <summary>Yields alerts as they arrive and a heartbeat whenever they do not.</summary>
    public static async IAsyncEnumerable<SseItem<object>> StreamAsync(
        Guid userId,
        IAlertStreamSubscriber subscriber,
        TimeProvider clock,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Unbounded: a burst of alerts for one user is a handful of small records, and a bounded
        // writer would have to choose between blocking the Redis worker and dropping an alert.
        var channel = Channel.CreateUnbounded<AlertNotification>();

        await using var subscription = await subscriber.SubscribeAsync(userId, channel.Writer, ct);

        using var heartbeat = new PeriodicTimer(HeartbeatInterval, clock);

        // One beat straight away, before anything can be waited on. Nothing is written to the socket
        // until the first frame, so the response headers do not leave the server either - and a client
        // behind a proxy that buffers until it sees bytes then sits on a connection it believes is
        // still opening. This costs one frame and makes "connected" observable immediately.
        yield return new SseItem<object>(new Heartbeat(clock.GetUtcNow()), PingEventName);

        // Both pending operations are held ACROSS iterations, and that is not a tidiness point.
        // ChannelReader.ReadAsync registers a waiter that consumes the next item; starting a fresh one
        // each pass and abandoning the old one hands the following alert to a task nobody awaits, and
        // the browser simply never sees it. Only whichever one completed is replaced.
        Task<AlertNotification>? next = null;
        Task<bool>? tick = null;

        while (!ct.IsCancellationRequested)
        {
            next ??= channel.Reader.ReadAsync(ct).AsTask();
            tick ??= heartbeat.WaitForNextTickAsync(ct).AsTask();

            SseItem<object> item;

            try
            {
                if (await Task.WhenAny(next, tick) == next)
                {
                    item = new SseItem<object>(await next, AlertEventName);
                    next = null;
                }
                else
                {
                    await tick;
                    tick = null;
                    item = new SseItem<object>(new Heartbeat(clock.GetUtcNow()), PingEventName);
                }
            }
            catch (OperationCanceledException)
            {
                // The browser closed the tab. That is how every one of these ends.
                yield break;
            }

            yield return item;
        }
    }

    /// <summary>The heartbeat payload. A named event with no listener is dropped before it reaches code.</summary>
    public sealed record Heartbeat(DateTimeOffset At);
}
