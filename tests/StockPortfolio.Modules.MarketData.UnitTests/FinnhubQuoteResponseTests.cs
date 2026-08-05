using System.Text.Json;
using Shouldly;
using StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

namespace StockPortfolio.Tests;

public sealed class FinnhubQuoteResponseTests
{
    private static FinnhubQuoteResponse Parse(string json) =>
        JsonSerializer.Deserialize<FinnhubQuoteResponse>(json).ShouldNotBeNull();

    [Fact]
    public void FinnhubResponse_NullDp_Deserialises()
    {
        var response = Parse("""{"c":187.42,"h":188.0,"l":186.1,"o":186.5,"pc":186.9,"d":null,"dp":null,"t":1780000000}""");

        response.Dp.ShouldBeNull();
        response.D.ShouldBeNull();
        response.Price.ShouldBe(187.42m);
    }

    [Fact]
    public void FinnhubResponse_MissingC_IsNotAPrice()
    {
        var response = Parse("""{"h":188.0,"l":186.1,"o":186.5,"pc":186.9,"t":1780000000}""");

        response.C.ShouldBeNull();
        response.Price.ShouldBeNull();
    }

    [Fact]
    public void FinnhubResponse_AllZero_IsNoPriceNotUnknownTicker()
    {
        var response = Parse("""{"c":0,"h":0,"l":0,"o":0,"pc":0,"d":null,"dp":null,"t":0}""");

        // No price this cycle, identical to a fetch failure. An all-zero body is never read as evidence
        // that the ticker does not exist - that question goes to /search, which can actually answer it.
        response.Price.ShouldBeNull();
    }

    [Fact]
    public void FinnhubTimestamp_ParsedAsSeconds() =>
        Parse("""{"c":1,"t":1780000000}""").TradeTimeUtc
            .ShouldBe(new DateTimeOffset(2026, 5, 28, 20, 26, 40, TimeSpan.Zero));

    [Fact]
    public void FinnhubTimestamp_MillisecondMagnitude_IsNotReadAsSeconds() =>
        Parse("""{"c":1,"t":1780000000000}""").TradeTimeUtc
            .ShouldBe(Parse("""{"c":1,"t":1780000000}""").TradeTimeUtc);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FinnhubTimestamp_NonPositive_IsNoTradeTime(long stamp) =>
        Parse($$"""{"c":1,"t":{{stamp}}}""").TradeTimeUtc.ShouldBeNull();
}
