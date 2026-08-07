using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Polling;

namespace StockPortfolio.Tests;

public sealed class RedisPollHeartbeatStoreTests
{
    private static readonly DateTimeOffset Finished = new(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Encode_RoundTripsAllThreeFields()
    {
        var encoded = RedisPollHeartbeatStore.Encode(new PollHeartbeat(Finished, 7, 5));

        encoded.ShouldBe(
            Finished.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture) + ":7:5");

        RedisPollHeartbeatStore.TryDecode(encoded, out var heartbeat).ShouldBeTrue();
        heartbeat.At.ShouldBe(Finished);
        heartbeat.TickersTargeted.ShouldBe(7);
        heartbeat.TickersStored.ShouldBe(5);
    }

    [Fact]
    public void Encode_AnEmptyCycle_RoundTripsAsZeroTargetsNotAsAbsent()
    {
        RedisPollHeartbeatStore.TryDecode(
            RedisPollHeartbeatStore.Encode(new PollHeartbeat(Finished, 0, 0)),
            out var heartbeat).ShouldBeTrue();

        heartbeat.TickersTargeted.ShouldBe(0);
        heartbeat.TickersStored.ShouldBe(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1780000000000")]
    [InlineData("1780000000000:7")]
    [InlineData("1780000000000:7:5:3")]
    [InlineData("not-a-stamp:7:5")]
    [InlineData("1780000000000:seven:5")]
    [InlineData("1780000000000:7:five")]
    public void Decode_CorruptValue_IsNoHeartbeatNotAThrow(string? encoded) =>
        RedisPollHeartbeatStore.TryDecode(encoded, out _).ShouldBeFalse();

    [Fact]
    public async Task Store_RedisUnreachable_SwallowsTheFailureOnBothPaths()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 50;
        options.ConnectRetry = 1;
        options.SyncTimeout = 50;

        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);

        var store = new RedisPollHeartbeatStore(multiplexer, NullLogger<RedisPollHeartbeatStore>.Instance);

        await Should.NotThrowAsync(() => store.WriteAsync(
            new PollHeartbeat(Finished, 7, 5),
            TestContext.Current.CancellationToken));

        (await store.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }
}
