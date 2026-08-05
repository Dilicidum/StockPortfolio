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
