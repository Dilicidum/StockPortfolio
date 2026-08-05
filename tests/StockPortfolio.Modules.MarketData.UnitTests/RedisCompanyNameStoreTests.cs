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

    /// <summary>A blank is not a name, so it is never written and never read back as one.</summary>
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

    /// <summary>A pathological provider answer must not be stored whole; the cap is silent, not a rejection.</summary>
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
        // A real multiplexer pointed at a dead port, exactly as AbortOnConnectFail=false leaves it in
        // production: every command throws RedisConnectionException at the call site.
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

        // Redis down means names disappear and nothing else changes — an empty map, never a throw.
        var read = await store.ReadAsync([ticker], TestContext.Current.CancellationToken);

        read.ShouldBeEmpty();
    }
}
