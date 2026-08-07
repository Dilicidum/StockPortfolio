using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Names;

namespace StockPortfolio.Tests;

public sealed class RedisCompanyNameStoreTests
{
    [Fact]
    public void Encode_TrimsAndKeepsTheName()
    {
        RedisCompanyNameStore.Encode("  Apple Inc  ").ShouldBe("Apple Inc");

        RedisCompanyNameStore.TryDecode("Apple Inc", out var name).ShouldBeTrue();
        name.ShouldBe("Apple Inc");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Encode_BlankName_IsNoNameNotAThrow(string? candidate)
    {
        RedisCompanyNameStore.Encode(candidate).ShouldBeNull();
        RedisCompanyNameStore.TryDecode(candidate, out _).ShouldBeFalse();
    }

    [Fact]
    public void Encode_AbsurdlyLongName_IsCappedRatherThanDropped()
    {
        var encoded = RedisCompanyNameStore.Encode(new string('x', 5000));

        encoded.ShouldNotBeNull();
        encoded.Length.ShouldBe(120);
    }

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

        var store = new RedisCompanyNameStore(multiplexer, NullLogger<RedisCompanyNameStore>.Instance);
        var ticker = Ticker.Create("AAPL").AsT0;

        await Should.NotThrowAsync(() => store.WriteAsync(
            [new SymbolMatch(ticker, "Apple Inc")],
            TestContext.Current.CancellationToken));

        var read = await store.ReadAsync([ticker], TestContext.Current.CancellationToken);

        read.ShouldBeEmpty();
    }
}
