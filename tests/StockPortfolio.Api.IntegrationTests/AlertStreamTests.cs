using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;

using Microsoft.Extensions.DependencyInjection;

using OneOf;

using StackExchange.Redis;

using StockPortfolio.Api.IntegrationTests.Infrastructure;
using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Streaming;
using StockPortfolio.Modules.Alerts.Application.Streaming.Commands.RedeemStreamTicket;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>The ticket handshake against real Redis, and the fan-out that nothing else proves.
///
/// Deliberately NOT over HTTP: TestServer holds a streaming response open until the server-side
/// enumerator ends, and the enumerator only ends when the client lets go — so reading the feed
/// through CreateClient() deadlocks. The frame names and the heartbeat are pinned by
/// StockPortfolio.Tests.AlertStreamTests instead, which drives the same enumerable directly.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class AlertStreamTests(ApiFixture fixture)
{
    private const string TicketPath = "/api/alerts/stream-ticket";
    private const string StreamPath = "/api/alerts/stream";

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>Single use, and it is the only security property a query-string credential has.</summary>
    [Fact]
    public async Task ATicket_RedeemsOnce_AndTheSecondAttemptFails()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "ticket-single-use");
        var userId = await UserIdAsync(client, token);

        var ticket = await TicketAsync(client, token);

        (await RedeemAsync(ticket)).AsT0.ShouldBe(userId);

        (await RedeemAsync(ticket)).IsT1.ShouldBeTrue(
            "the redeem is one StringGetDeleteAsync. A GET followed by a DEL would let two connections "
                + "both read the ticket before either deleted it, and single use is the whole of what "
                + "makes a credential in a query string tolerable.");
    }

    /// <summary>The expiry is Redis's own, so the route never re-checks a lifetime it did not set.</summary>
    [Fact]
    public async Task AnExpiredTicket_Fails()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "ticket-expiry");
        var userId = await UserIdAsync(client, token);

        var expiring = "expiring-" + Guid.NewGuid().ToString("N");

        // A second rather than the route's thirty, so this test costs a second rather than half a minute.
        await _fixture.Services.GetRequiredService<IStreamTicketStore>()
            .IssueAsync(expiring, userId, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        (await RedeemAsync(expiring)).AsT0.ShouldBe(userId, "it must work before it expires.");

        var again = "expiring-" + Guid.NewGuid().ToString("N");

        await _fixture.Services.GetRequiredService<IStreamTicketStore>()
            .IssueAsync(again, userId, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        await Task.Delay(TimeSpan.FromSeconds(1.5), TestContext.Current.CancellationToken);

        (await RedeemAsync(again)).IsT1.ShouldBeTrue();
    }

    /// <summary>Expired, spent and never-issued deliberately get one answer, and the route's is 401.</summary>
    [Fact]
    public async Task AnInventedTicket_IsRefusedByTheRoute()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.SendAsync(
            client,
            HttpMethod.Get,
            $"{StreamPath}?ticket={Uri.EscapeDataString("never-issued-by-this-host")}");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>The stream is the one anonymous route, so no ticket at all is still a 401 rather than a 400.</summary>
    [Fact]
    public async Task TheStream_WithNoTicketAtAll_IsRefused()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.SendAsync(client, HttpMethod.Get, StreamPath);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }

    /// <summary>THE fan-out test. An alert produced on another connection reaches this replica's subscriber.</summary>
    [Fact]
    public async Task AnAlertPublishedOnAnotherConnection_ReachesASubscribedStream()
    {
        using var client = _fixture.CreateClient();
        var token = await SignedInAsync(client, "fan-out");
        var userId = await UserIdAsync(client, token);

        var channel = Channel.CreateUnbounded<AlertNotification>();

        await using var subscription = await _fixture.Services
            .GetRequiredService<IAlertStreamSubscriber>()
            .SubscribeAsync(userId, channel.Writer, TestContext.Current.CancellationToken);

        // A SECOND multiplexer, and that is the entire point: an in-process hand-off would pass this
        // test only if the publisher and the subscriber shared a field, which is exactly the design
        // that loses an alert produced on replica A while the user's stream is held by replica B.
        await using var elsewhere = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisConnectionString);

        var redisChannel = RedisChannel.Literal(
            string.Create(CultureInfo.InvariantCulture, $"alerts:user:{userId:D}"));

        var payload = JsonSerializer.Serialize(
            new
            {
                id = Guid.NewGuid(),
                userId,
                ticker = "AAPL",
                direction = "Fall",
                changePercent = "-6.00",
                endpointPercent = "-6.00",
                triggerPrice = "141",
                referencePrice = "150",
                currency = "USD",
                firedAt = DateTimeOffset.UtcNow,
                isSimulated = false,
                reason = "fell 6% from the window high",
            },
            JsonSerializerOptions.Web);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var arrival = channel.Reader.ReadAsync(cts.Token).AsTask();

        // Published until somebody is listening: subscribing is a round trip to Redis and the first
        // publish can beat it, in which case pub/sub drops the message rather than queueing it.
        while (!arrival.IsCompleted
            && await elsewhere.GetSubscriber().PublishAsync(redisChannel, payload) == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cts.Token);
        }

        var received = await arrival;

        received.Ticker.ShouldBe("AAPL");
        received.Direction.ShouldBe("Fall");
        received.UserId.ShouldBe(userId);
    }

    private async Task<OneOf<Guid, TicketNotRecognised>> RedeemAsync(string ticket)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<ICommandHandler<RedeemStreamTicketCommand, OneOf<Guid, TicketNotRecognised>>>()
            .Handle(new RedeemStreamTicketCommand(ticket), TestContext.Current.CancellationToken);
    }

    private static async Task<string> SignedInAsync(HttpClient client, string prefix) =>
        (await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail(prefix))).AccessToken;

    private static async Task<Guid> UserIdAsync(HttpClient client, string accessToken)
    {
        using var response = await Wire.SendAsync(client, HttpMethod.Get, "/api/auth/me", accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var payload = await response.Content.ReadFromJsonAsync<UserPayload>(JsonSerializerOptions.Web);

        payload.ShouldNotBeNull();

        // Alerts keys a fired alert on a uuid, so this parse matches what its endpoints do.
        return payload.Id;
    }

    private static async Task<string> TicketAsync(HttpClient client, string accessToken)
    {
        // No body at all, exactly as logout is sent: a ticket request has no input.
        using var response = await Wire.SendAsync(client, HttpMethod.Post, TicketPath, accessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var payload = await response.Content.ReadFromJsonAsync<StreamTicketPayload>(JsonSerializerOptions.Web);

        payload.ShouldNotBeNull();
        payload.Ticket.ShouldNotBeNullOrWhiteSpace();
        payload.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        return payload.Ticket;
    }

    /// <summary>What POST /api/alerts/stream-ticket answers with.</summary>
    private sealed record StreamTicketPayload(string Ticket, DateTimeOffset ExpiresAt);
}
