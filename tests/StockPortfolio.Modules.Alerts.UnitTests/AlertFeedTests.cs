using System.Threading.Channels;

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;

using Shouldly;

using StockPortfolio.Modules.Alerts.Api;
using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;

namespace StockPortfolio.Tests;

/// <summary>The frame names, the opening beat, and the read that must survive a heartbeat.</summary>
public sealed class AlertFeedTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 14, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeAlertStreamSubscriber _subscriber = new();

    /// <summary>Nothing is written until the first frame, so a stream with no beat has not connected.</summary>
    [Fact]
    public async Task TheFirstFrame_ArrivesBeforeAnythingIsWaitedOn()
    {
        using var cts = new CancellationTokenSource();

        await using var frames = Stream(cts.Token).GetAsyncEnumerator(cts.Token);

        (await frames.MoveNextAsync()).ShouldBeTrue();

        frames.Current.EventType.ShouldBe(
            AlertFeed.PingEventName,
            "response headers do not leave the server until a frame does, so a stream whose first "
                + "frame is twenty seconds away looks like a connection that is still opening.");

        await cts.CancelAsync();
    }

    /// <summary>A pushed alert is a NAMED alert event. Unnamed, it arrives as "message" and is lost.</summary>
    [Fact]
    public async Task APushedAlert_IsANamedAlertEvent()
    {
        using var cts = new CancellationTokenSource();

        await using var frames = Stream(cts.Token).GetAsyncEnumerator(cts.Token);

        (await frames.MoveNextAsync()).ShouldBeTrue();

        _subscriber.Push(Notification("AAPL"));

        (await frames.MoveNextAsync()).ShouldBeTrue();

        frames.Current.EventType.ShouldBe(
            AlertFeed.AlertEventName,
            "the client listens for 'alert' by name. An unnamed frame is delivered as 'message', which "
                + "no listener is registered for, so the alert is dropped before it reaches any code.");

        ((AlertNotification)frames.Current.Data).Ticker.ShouldBe("AAPL");

        await cts.CancelAsync();
    }

    /// <summary>The beat is a real event because SseFormatter has no comment API — and it is timer-driven.</summary>
    [Fact]
    public async Task AQuietStream_BeatsOnTheHeartbeatInterval()
    {
        using var cts = new CancellationTokenSource();

        await using var frames = Stream(cts.Token).GetAsyncEnumerator(cts.Token);

        (await frames.MoveNextAsync()).ShouldBeTrue();

        var next = frames.MoveNextAsync();

        _clock.Advance(AlertFeed.HeartbeatInterval);

        (await next).ShouldBeTrue();
        frames.Current.EventType.ShouldBe(AlertFeed.PingEventName);

        await cts.CancelAsync();
    }

    /// <summary>The one that a fresh ReadAsync every pass would break: an alert during a beat is not eaten.</summary>
    [Fact]
    public async Task AnAlertThatArrivesAfterAHeartbeat_IsStillDelivered()
    {
        using var cts = new CancellationTokenSource();

        await using var frames = Stream(cts.Token).GetAsyncEnumerator(cts.Token);

        (await frames.MoveNextAsync()).ShouldBeTrue();

        // One beat, which is where a loop that starts a new channel read every pass abandons the old
        // one. An abandoned ReadAsync still consumes the next item, so the alert below would be handed
        // to a task nobody awaits and the browser would never see it.
        var beat = frames.MoveNextAsync();
        _clock.Advance(AlertFeed.HeartbeatInterval);
        (await beat).ShouldBeTrue();
        frames.Current.EventType.ShouldBe(AlertFeed.PingEventName);

        _subscriber.Push(Notification("MSFT"));

        (await frames.MoveNextAsync()).ShouldBeTrue();
        frames.Current.EventType.ShouldBe(AlertFeed.AlertEventName);
        ((AlertNotification)frames.Current.Data).Ticker.ShouldBe("MSFT");

        await cts.CancelAsync();
    }

    /// <summary>The browser closing the tab ends the stream, and unsubscribes on the way out.</summary>
    [Fact]
    public async Task ACancelledStream_EndsAndUnsubscribes()
    {
        using var cts = new CancellationTokenSource();

        await using (var frames = Stream(cts.Token).GetAsyncEnumerator(cts.Token))
        {
            (await frames.MoveNextAsync()).ShouldBeTrue();

            var pending = frames.MoveNextAsync();

            await cts.CancelAsync();

            (await pending).ShouldBeFalse();
        }

        _subscriber.Unsubscribed.ShouldBeTrue(
            "a subscription left behind is a Redis channel this replica keeps listening to for a "
                + "connection that is gone.");
    }

    /// <summary>The overload that shipped broken once. Typed on the wrapper, every frame arrives unnamed.</summary>
    [Fact]
    public void TheResult_IsTypedOnThePayload_NotOnTheFrameWrapper()
    {
        using var cts = new CancellationTokenSource();

        AlertFeed.Result(UserId, _subscriber, _clock, cts.Token)
            .ShouldBeOfType<ServerSentEventsResult<object>>(
                "TypedResults.ServerSentEvents has two overloads and IAsyncEnumerable<SseItem<T>> "
                    + "satisfies both. Selecting the IAsyncEnumerable<T> one gives "
                    + "ServerSentEventsResult<SseItem<object>>, which writes the wrapper as the frame's "
                    + "data and no event name at all — so a client listening for 'alert' receives "
                    + "nothing, and every assertion about the enumerable still passes.");
    }

    private static AlertNotification Notification(string ticker) => new(
        Guid.NewGuid(),
        UserId,
        ticker,
        "Fall",
        "-6.00",
        "-6.00",
        "141",
        "150",
        "USD",
        Now,
        IsSimulated: false,
        "fell 6% from the window high");

    private IAsyncEnumerable<System.Net.ServerSentEvents.SseItem<object>> Stream(CancellationToken ct) =>
        AlertFeed.StreamAsync(UserId, _subscriber, _clock, ct);

    /// <summary>Stands in for Redis: a test pushes into the same writer the real subscriber feeds.</summary>
    private sealed class FakeAlertStreamSubscriber : IAlertStreamSubscriber
    {
        private ChannelWriter<AlertNotification>? _writer;

        /// <summary>Gets whether the handle was disposed, which is what unsubscribes.</summary>
        public bool Unsubscribed { get; private set; }

        public Task<IAsyncDisposable> SubscribeAsync(
            Guid userId,
            ChannelWriter<AlertNotification> writer,
            CancellationToken ct)
        {
            _writer = writer;

            return Task.FromResult<IAsyncDisposable>(new Handle(this));
        }

        public void Push(AlertNotification notification) => _writer!.TryWrite(notification);

        private sealed class Handle(FakeAlertStreamSubscriber owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Unsubscribed = true;

                return ValueTask.CompletedTask;
            }
        }
    }
}
