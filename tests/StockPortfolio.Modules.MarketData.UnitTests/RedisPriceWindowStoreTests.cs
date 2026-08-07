using System.Globalization;

using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Infrastructure.Prices;

namespace StockPortfolio.Tests;

public sealed class RedisPriceWindowStoreTests
{
    private static readonly DateTimeOffset Observed = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Encode_TwoSamplesAtOnePrice_AreTwoMembersNotOne()
    {
        // A sorted set keys on the member: a bare price collides the second time a ticker prints it, silently erasing the earlier reading.
        var members = new HashSet<string>(StringComparer.Ordinal)
        {
            RedisPriceWindowStore.Encode(187.42m, Observed),
            RedisPriceWindowStore.Encode(187.42m, Observed.AddMinutes(1)),
        };

        members.Count.ShouldBe(2);
    }

    [Fact]
    public void Encode_RoundTripsScaleAndStamp()
    {
        // decimal carries its own scale, so 187.4200m must come back as 187.4200m, not 187.42m.
        var encoded = RedisPriceWindowStore.Encode(187.4200m, Observed);

        encoded.ShouldBe(
            Observed.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + ":187.4200");

        RedisPriceWindowStore.TryDecode(encoded, out var sample).ShouldBeTrue();
        sample.At.ShouldBe(Observed);
        sample.Price.ShouldBe(187.4200m);
        sample.Price.ToString(CultureInfo.InvariantCulture).ShouldBe("187.4200");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("187.42")]
    [InlineData("not-a-sample")]
    [InlineData(":187.42")]
    [InlineData("1780000000000:")]
    [InlineData("1780000000000:187,42")]
    [InlineData("not-a-stamp:187.42")]
    public void Decode_CorruptMember_IsNoSampleNotAThrow(string? encoded) =>
        RedisPriceWindowStore.TryDecode(encoded, out _).ShouldBeFalse();

    [Fact]
    public async Task Store_RedisUnreachable_SwallowsTheFailureOnBothPaths()
    {
        // A real multiplexer on a dead port: with AbortOnConnectFail=false every command throws at the call site, as in production.
        var options = ConfigurationOptions.Parse("127.0.0.1:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 50;
        options.ConnectRetry = 1;
        options.SyncTimeout = 50;

        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);

        var store = new RedisPriceWindowStore(multiplexer, NullLogger<RedisPriceWindowStore>.Instance);

        await Should.NotThrowAsync(() => store.AppendAsync(
            "AAPL",
            187.42m,
            Observed,
            TimeSpan.FromMinutes(75),
            TestContext.Current.CancellationToken));

        var samples = await store.ReadAsync(
            "AAPL",
            Observed.AddMinutes(-60),
            TestContext.Current.CancellationToken);

        samples.ShouldBeEmpty();
    }
}
