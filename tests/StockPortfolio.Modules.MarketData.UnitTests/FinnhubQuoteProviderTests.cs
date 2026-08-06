using System.Net;
using System.Threading.RateLimiting;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

namespace StockPortfolio.Tests;

public sealed class FinnhubQuoteProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    // AutoReplenishment off: its timer takes no TimeProvider, so FakeTimeProvider cannot advance it.
    private static TokenBucketRateLimiter FullBucket() =>
        new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = 25,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            AutoReplenishment = false,
            QueueLimit = 256,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });

    private static FinnhubQuoteProvider Build(CountingHandler handler, TokenBucketRateLimiter bucket) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") },
            bucket,
            new FakeTimeProvider(Now),
            NullLogger<FinnhubQuoteProvider>.Instance);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Finnhub401And403_AreNotRetried(HttpStatusCode status)
    {
        var handler = new CountingHandler(status, """{"error":"You don't have access to this resource."}""");
        using var bucket = FullBucket();
        var provider = Build(handler, bucket);

        var quotes = await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            TestContext.Current.CancellationToken);

        quotes.ShouldBeEmpty();
        handler.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task SymbolExists_WhenTheKeyIsRejected_FailsOpen(HttpStatusCode status)
    {
        using var bucket = FullBucket();
        var provider = Build(new CountingHandler(status), bucket);

        var exists = await provider.SymbolExistsAsync(
            Ticker.Create("AAPL").AsT0,
            TestContext.Current.CancellationToken);

        exists.ShouldBeTrue();
    }

    /// <summary>The fake never produces UnknownTicker, so the real mapping is only ever asserted here.</summary>
    [Theory]
    [InlineData("""{"count":1,"result":[{"symbol":"AAPL"}]}""", true)]
    [InlineData("""{"count":1,"result":[{"symbol":"aapl"}]}""", true)]
    [InlineData("""{"count":2,"result":[{"symbol":"AAPLW"},{"symbol":"AAPL.SW"}]}""", false)]
    [InlineData("""{"count":0,"result":[]}""", false)]
    public async Task SymbolExists_IsAnExactMatchOnSymbol_NeverACountOfFuzzyHits(string body, bool expected)
    {
        using var bucket = FullBucket();
        var provider = Build(new CountingHandler(HttpStatusCode.OK, body), bucket);

        var exists = await provider.SymbolExistsAsync(
            Ticker.Create("AAPL").AsT0,
            TestContext.Current.CancellationToken);

        exists.ShouldBe(expected);
    }

    [Fact]
    public async Task SymbolExists_WhenTheSearchTransportFails_FailsOpen()
    {
        using var bucket = FullBucket();

        var provider = new FinnhubQuoteProvider(
            new HttpClient(new FlakyHandler("AAPL")) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") },
            bucket,
            new FakeTimeProvider(Now),
            NullLogger<FinnhubQuoteProvider>.Instance);

        var exists = await provider.SymbolExistsAsync(
            Ticker.Create("AAPL").AsT0,
            TestContext.Current.CancellationToken);

        // An outage must never reject a valid purchase; a degraded read is not a broken write.
        exists.ShouldBeTrue();
    }

    /// <summary>Search keeps what the existence check throws away — the whole point of reusing that call.</summary>
    [Fact]
    public async Task Search_KeepsTheDescriptionTheExistenceCheckDiscards()
    {
        const string Body = """
            {"count":2,"result":[
              {"symbol":"AAPL","description":"APPLE INC"},
              {"symbol":"APLE","description":"APPLE HOSPITALITY REIT INC"}]}
            """;

        using var bucket = FullBucket();
        var provider = Build(new CountingHandler(HttpStatusCode.OK, Body), bucket);

        var matches = await provider.SearchSymbolsAsync("appl", TestContext.Current.CancellationToken);

        matches.Select(match => match.Ticker.Value).ShouldBe(["AAPL", "APLE"]);
        matches[0].Name.ShouldBe("APPLE INC");
        matches[1].Name.ShouldBe("APPLE HOSPITALITY REIT INC");
    }

    /// <summary>A suggestion the add-position form would then reject is worse than no suggestion.</summary>
    [Fact]
    public async Task Search_DropsRowsThatCouldNotBeAdded_AndDeduplicates()
    {
        const string Body = """
            {"count":6,"result":[
              {"symbol":"AAPL","description":"APPLE INC"},
              {"symbol":"AAPL.SW","description":"APPLE INC SWISS"},
              {"symbol":"AAPL34.SA","description":"APPLE INC BDR"},
              {"symbol":"AAPL","description":"APPLE INC (SECOND LISTING)"},
              {"symbol":"TOOLONG","description":"SEVEN LETTERS"},
              {"symbol":"NONAME","description":"   "}]}
            """;

        using var bucket = FullBucket();
        var provider = Build(new CountingHandler(HttpStatusCode.OK, Body), bucket);

        var matches = await provider.SearchSymbolsAsync("aapl", TestContext.Current.CancellationToken);

        // One AAPL, and nothing else: the fuzzy hits are wanted, the unaddable rows are not.
        matches.ShouldHaveSingleItem().Ticker.Value.ShouldBe("AAPL");
        matches[0].Name.ShouldBe("APPLE INC");
    }

    /// <summary>An outage must leave the field a plain text box, never break the form it sits on.</summary>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Search_WhenTheKeyIsRejected_IsEmptyNotAThrow(HttpStatusCode status)
    {
        using var bucket = FullBucket();
        var provider = Build(new CountingHandler(status), bucket);

        (await provider.SearchSymbolsAsync("appl", TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_WhenTheTransportFails_IsEmptyNotAThrow()
    {
        using var bucket = FullBucket();

        var provider = new FinnhubQuoteProvider(
            new HttpClient(new FlakyHandler("appl")) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") },
            bucket,
            new FakeTimeProvider(Now),
            NullLogger<FinnhubQuoteProvider>.Instance);

        (await provider.SearchSymbolsAsync("appl", TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    /// <summary>A body with no result array at all is "nothing matched", not a null reference.</summary>
    [Theory]
    [InlineData("""{"count":0,"result":[]}""")]
    [InlineData("""{"count":0}""")]
    [InlineData("{}")]
    public async Task Search_EmptyOrShapelessBody_IsAnEmptyList(string body)
    {
        using var bucket = FullBucket();
        var provider = Build(new CountingHandler(HttpStatusCode.OK, body), bucket);

        (await provider.SearchSymbolsAsync("zzzz", TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Fetch_StampsObservedAtWithOurClock_NotFinnhubsTradeTime()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, """{"c":187.42,"t":1000000000}""");
        using var bucket = FullBucket();
        var provider = Build(handler, bucket);

        var quotes = await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            TestContext.Current.CancellationToken);

        quotes.ShouldHaveSingleItem().ObservedAt.ShouldBe(Now);
        quotes[0].Price.ShouldBe(187.42m);
    }

    [Fact]
    public async Task Fetch_OneTickerFailing_DoesNotDiscardTheOthers()
    {
        var handler = new FlakyHandler("MSFT");
        using var bucket = FullBucket();
        var provider = new FinnhubQuoteProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") },
            bucket,
            new FakeTimeProvider(Now),
            NullLogger<FinnhubQuoteProvider>.Instance);

        var quotes = await provider.GetQuotesAsync(
            new HashSet<Ticker>
            {
                Ticker.Create("AAPL").AsT0,
                Ticker.Create("MSFT").AsT0,
                Ticker.Create("TSLA").AsT0,
            },
            TestContext.Current.CancellationToken);

        quotes.Select(quote => quote.Ticker.Value).Order(StringComparer.Ordinal).ShouldBe(["AAPL", "TSLA"]);
    }

    private sealed class FlakyHandler(string failingSymbol) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            request.RequestUri!.Query.Contains(failingSymbol, StringComparison.Ordinal)
                ? throw new HttpRequestException("upstream refused")
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"c":187.42,"t":1780000000}""", System.Text.Encoding.UTF8, "application/json"),
                });
    }
}
