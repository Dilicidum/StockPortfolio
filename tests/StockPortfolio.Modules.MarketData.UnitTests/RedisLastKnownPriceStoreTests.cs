using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Prices;

namespace StockPortfolio.Tests;

public sealed class RedisLastKnownPriceStoreTests
{
    private static readonly DateTimeOffset Observed = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Encode_RoundTripsScale()
    {
        // decimal carries its own scale, so 187.4200m must come back as 187.4200m, not 187.42m.
        var encoded = RedisLastKnownPriceStore.Encode(187.4200m, Observed);

        encoded.ShouldBe("187.4200:" + Observed.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

        RedisLastKnownPriceStore.TryDecode(encoded, out var price).ShouldBeTrue();
        price.Price.ShouldBe(187.4200m);
        price.Price.ToString(System.Globalization.CultureInfo.InvariantCulture).ShouldBe("187.4200");
        price.ObservedAt.ShouldBe(Observed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-price")]
    [InlineData("187.42")]
    [InlineData(":1780000000000")]
    [InlineData("187,42:1780000000000")]
    [InlineData("187.42:not-a-stamp")]
    public void Decode_CorruptValue_IsNoPriceNotAThrow(string? encoded) =>
        RedisLastKnownPriceStore.TryDecode(encoded, out _).ShouldBeFalse();

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

        var store = new RedisLastKnownPriceStore(multiplexer, NullLogger<RedisLastKnownPriceStore>.Instance);
        var ticker = Ticker.Create("AAPL").AsT0;

        await Should.NotThrowAsync(() => store.WriteAsync(
            [new Quote(ticker, 187.42m, Observed)],
            TestContext.Current.CancellationToken));

        var read = await store.ReadAsync([ticker], TestContext.Current.CancellationToken);

        read.ShouldBeEmpty();
    }
}
