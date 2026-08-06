using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
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
            apiKeyOverride: null,
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
    public async Task FakeProvider_ApiKeyOverride_IsIgnored()
    {
        var ticker = new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 };

        var withOverride = await Build(Now).GetQuotesAsync(
            ticker, "an-override-key", TestContext.Current.CancellationToken);

        var withoutOverride = await Build(Now).GetQuotesAsync(
            ticker, apiKeyOverride: null, TestContext.Current.CancellationToken);

        // Two fresh instances, same clock: identical output is only possible if the override changed nothing.
        withOverride.ShouldHaveSingleItem().Price.ShouldBe(withoutOverride.ShouldHaveSingleItem().Price);
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

    /// <summary>`docker compose up` with no key is the acceptance gate, so search must work without one.</summary>
    [Theory]
    [InlineData("appl", "AAPL")]
    [InlineData("APPLE", "AAPL")]
    [InlineData("aap", "AAPL")]
    [InlineData("micro", "MSFT")]
    [InlineData("tsl", "TSLA")]
    public async Task FakeProvider_Search_FindsBySymbolPrefixOrName(string query, string expected)
    {
        var matches = await Build(Now).SearchSymbolsAsync(query, TestContext.Current.CancellationToken);

        matches.Select(match => match.Ticker.Value).ShouldContain(expected);
    }

    [Fact]
    public async Task FakeProvider_Search_CarriesAName()
    {
        var matches = await Build(Now).SearchSymbolsAsync("aapl", TestContext.Current.CancellationToken);

        matches.ShouldHaveSingleItem().Name.ShouldBe("Apple Inc");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zzzzz")]
    public async Task FakeProvider_Search_NothingMatching_IsAnEmptyList(string query) =>
        (await Build(Now).SearchSymbolsAsync(query, TestContext.Current.CancellationToken)).ShouldBeEmpty();

    /// <summary>Every catalogue symbol must be addable, or picking one fills a field the form rejects.</summary>
    [Fact]
    public async Task FakeProvider_EverySuggestion_IsASymbolThisAppCanHold()
    {
        var provider = Build(Now);
        var everything = new List<string>();

        foreach (var letter in "ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            var matches = await provider.SearchSymbolsAsync(
                letter.ToString(),
                TestContext.Current.CancellationToken);

            everything.AddRange(matches.Select(match => match.Ticker.Value));
        }

        everything.ShouldNotBeEmpty("a single-letter query matched nothing at all, so this proves nothing");
        everything.ShouldAllBe(symbol => Ticker.Create(symbol).IsT0);
    }

    /// <summary>The fake is the only provider on the clean-clone path, so it needs a rule that can say no.</summary>
    [Theory]
    [InlineData("sixteen-character", true)]
    [InlineData("too-short", false)]
    [InlineData("unknown", false)]
    public async Task FakeProvider_VerifyKey_AcceptsSixteenOrMoreCharacters(string key, bool accepted)
    {
        var verdict = await Build(Now).VerifyKeyAsync(key, TestContext.Current.CancellationToken);

        verdict.ShouldBe(accepted ? KeyVerdict.Accepted : KeyVerdict.Rejected);
    }

    /// <summary>There is no Unknown verdict from the fake: a fake that pretends the provider is down is
    /// a fake nobody can reason about.</summary>
    [Fact]
    public async Task FakeProvider_VerifyKey_NeverAnswersUnknown()
    {
        var verdict = await Build(Now).VerifyKeyAsync("way-more-than-sixteen-characters", TestContext.Current.CancellationToken);

        verdict.ShouldNotBe(KeyVerdict.Unknown);
    }

    [Fact]
    public void FakeProvider_Hash_IsTheDocumentedFnv1a()
    {
        // The canonical FNV-1a 32-bit vector. If this drifts, every generated price silently moves.
        FakeQuoteProvider.Fnv1a("a").ShouldBe(0xe40c292cu);
        FakeQuoteProvider.Fnv1a("foobar").ShouldBe(0xbf9cf968u);
    }
}
