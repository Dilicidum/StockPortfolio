using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

namespace StockPortfolio.Tests;

public sealed class FakeQuoteProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 34, 0, TimeSpan.Zero);

    private static FakeQuoteProvider Build(DateTimeOffset now) =>
        new(
            FakeQuoteOptions.FromConfiguration(new ConfigurationBuilder().Build()),
            new FakeTimeProvider(now));

    private static async Task<decimal> PriceOf(FakeQuoteProvider provider, string ticker)
    {
        var quotes = await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create(ticker).AsT0 },
            TestContext.Current.CancellationToken);

        return quotes.ShouldHaveSingleItem().Price;
    }

    [Fact]
    public async Task FakeProvider_SameTickerSameMinute_SamePriceAcrossTwoInstances()
    {
        // Two instances, not two calls: same-instance equality passes with string.GetHashCode() too,
        // so only this form pins the FNV-1a requirement that survives a process restart.
        var first = await PriceOf(Build(Now), "AAPL");
        var second = await PriceOf(Build(Now), "AAPL");

        second.ShouldBe(first);
    }

    [Fact]
    public async Task FakeProvider_DifferentTickers_GetDifferentPrices()
    {
        var provider = Build(Now);

        (await PriceOf(provider, "AAPL")).ShouldNotBe(await PriceOf(provider, "MSFT"));
    }

    [Fact]
    public async Task FakeProvider_BasePrice_LandsInTheAdvertisedRange()
    {
        // Minute zero is the base price itself, before any walk step.
        var price = await PriceOf(Build(new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero)), "AAPL");

        price.ShouldBeInRange(20m, 500m);
    }

    [Fact]
    public async Task FakeProvider_LaterMinute_MovesThePrice()
    {
        var early = await PriceOf(Build(new DateTimeOffset(2026, 8, 5, 0, 1, 0, TimeSpan.Zero)), "AAPL");
        var later = await PriceOf(Build(new DateTimeOffset(2026, 8, 5, 6, 0, 0, TimeSpan.Zero)), "AAPL");

        later.ShouldNotBe(early);
    }

    [Fact]
    public async Task FakeProvider_AnyWellShapedSymbol_Exists() =>
        (await Build(Now).SymbolExistsAsync(
            Ticker.Create("ZZZZ").AsT0,
            TestContext.Current.CancellationToken)).ShouldBeTrue();

    [Fact]
    public async Task FakeProvider_Nudge_ShiftsThePriceUntilItExpires()
    {
        var provider = Build(Now);
        var before = await PriceOf(provider, "AAPL");

        provider.Nudge("aapl", 10m, TimeSpan.FromMinutes(5));

        (await PriceOf(provider, "AAPL")).ShouldBeGreaterThan(before);
    }

    [Fact]
    public void FakeProvider_Hash_IsTheDocumentedFnv1a()
    {
        // The canonical FNV-1a 32-bit vector. If this drifts, every generated price silently moves.
        FakeQuoteProvider.Fnv1a("a").ShouldBe(0xe40c292cu);
        FakeQuoteProvider.Fnv1a("foobar").ShouldBe(0xbf9cf968u);
    }
}
