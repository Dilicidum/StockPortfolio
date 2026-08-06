using System.Net;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

namespace StockPortfolio.Tests;

public sealed class FinnhubQuoteProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    private static FinnhubQuoteProvider Build(CountingHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") },
            new StaticHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") }),
            new FakeTimeProvider(Now),
            NullLogger<FinnhubQuoteProvider>.Instance);

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Finnhub401And403_AreNotRetried(HttpStatusCode status)
    {
        var handler = new CountingHandler(status, """{"error":"You don't have access to this resource."}""");
        var provider = Build(handler);

        var quotes = await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            apiKeyOverride: null,
            TestContext.Current.CancellationToken);

        quotes.ShouldBeEmpty();
        handler.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task SymbolExists_WhenTheKeyIsRejected_FailsOpen(HttpStatusCode status)
    {
        var provider = Build(new CountingHandler(status));

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
        var provider = Build(new CountingHandler(HttpStatusCode.OK, body));

        var exists = await provider.SymbolExistsAsync(
            Ticker.Create("AAPL").AsT0,
            TestContext.Current.CancellationToken);

        exists.ShouldBe(expected);
    }

    [Fact]
    public async Task SymbolExists_WhenTheSearchTransportFails_FailsOpen()
    {
        var provider = BuildWithFlaky(new FlakyHandler("AAPL"));

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

        var provider = Build(new CountingHandler(HttpStatusCode.OK, Body));

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

        var provider = Build(new CountingHandler(HttpStatusCode.OK, Body));

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
        var provider = Build(new CountingHandler(status));

        (await provider.SearchSymbolsAsync("appl", TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_WhenTheTransportFails_IsEmptyNotAThrow()
    {
        var provider = BuildWithFlaky(new FlakyHandler("appl"));

        (await provider.SearchSymbolsAsync("appl", TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    /// <summary>A body with no result array at all is "nothing matched", not a null reference.</summary>
    [Theory]
    [InlineData("""{"count":0,"result":[]}""")]
    [InlineData("""{"count":0}""")]
    [InlineData("{}")]
    public async Task Search_EmptyOrShapelessBody_IsAnEmptyList(string body)
    {
        var provider = Build(new CountingHandler(HttpStatusCode.OK, body));

        (await provider.SearchSymbolsAsync("zzzz", TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Fetch_StampsObservedAtWithOurClock_NotFinnhubsTradeTime()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, """{"c":187.42,"t":1000000000}""");
        var provider = Build(handler);

        var quotes = await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            apiKeyOverride: null,
            TestContext.Current.CancellationToken);

        quotes.ShouldHaveSingleItem().ObservedAt.ShouldBe(Now);
        quotes[0].Price.ShouldBe(187.42m);
    }

    [Fact]
    public async Task Fetch_OneTickerFailing_DoesNotDiscardTheOthers()
    {
        var provider = BuildWithFlaky(new FlakyHandler("MSFT"));

        var quotes = await provider.GetQuotesAsync(
            new HashSet<Ticker>
            {
                Ticker.Create("AAPL").AsT0,
                Ticker.Create("MSFT").AsT0,
                Ticker.Create("TSLA").AsT0,
            },
            apiKeyOverride: null,
            TestContext.Current.CancellationToken);

        quotes.Select(quote => quote.Ticker.Value).Order(StringComparer.Ordinal).ShouldBe(["AAPL", "TSLA"]);
    }

    /// <summary>Requirement 10's second gap: a WAF/CDN page must degrade the ticker, not crash the request.</summary>
    [Fact]
    public async Task GetQuotes_WhenTheProviderReturnsHtmlWithA200_OmitsTheTickerRatherThanThrowing()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, body: "<html>Access denied</html>", contentType: "text/html");
        var provider = Build(handler);

        var quotes = await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            apiKeyOverride: null,
            TestContext.Current.CancellationToken);

        quotes.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetQuotes_WhenTheProviderReturnsMalformedJson_OmitsTheTickerRatherThanThrowing()
    {
        var handler = new CountingHandler(HttpStatusCode.OK, body: "{ not json", contentType: "application/json");
        var provider = Build(handler);

        var quotes = await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            apiKeyOverride: null,
            TestContext.Current.CancellationToken);

        quotes.ShouldBeEmpty();
    }

    /// <summary>The test this task exists for: a per-user key must trip a breaker that belongs to it alone,
    /// which is only true if it travels over a different client than the one the app's own key and the
    /// poller share. A header alone on the shared client would not give that isolation.</summary>
    [Fact]
    public async Task GetQuotes_WithAnApiKeyOverride_RoutesThroughTheByokNamedClient()
    {
        var sharedHandler = new CountingHandler(HttpStatusCode.OK, """{"c":1.23,"t":1000000000}""");
        var byokHandler = new CountingHandler(HttpStatusCode.OK, """{"c":4.56,"t":1000000000}""");

        var sharedClient = new HttpClient(sharedHandler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") };
        var byokClient = new HttpClient(byokHandler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") };

        var factory = new StaticHttpClientFactory(byokClient);
        var provider = new FinnhubQuoteProvider(sharedClient, factory, new FakeTimeProvider(Now), NullLogger<FinnhubQuoteProvider>.Instance);

        var quotes = await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            "a-users-own-key",
            TestContext.Current.CancellationToken);

        sharedHandler.Calls.ShouldBe(0, "an override key must never touch the client every dashboard and the poller share");
        byokHandler.Calls.ShouldBe(1);
        quotes.ShouldHaveSingleItem().Price.ShouldBe(4.56m);
        factory.RequestedName.ShouldBe(FinnhubQuoteProvider.ByokClientName);
    }

    /// <summary>The header carries the key, not the client: a stale default on the shared client must never leak.</summary>
    [Fact]
    public async Task GetQuotes_WithAnApiKeyOverride_SendsItOnTheRequest_NeverTheSharedClientsDefault()
    {
        var headerHandler = new HeaderCapturingHandler();
        var byokClient = new HttpClient(headerHandler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") };

        var sharedClient = new HttpClient(new CountingHandler(HttpStatusCode.OK, """{"c":1.23,"t":1000000000}"""))
        {
            BaseAddress = new Uri("https://api.finnhub.io/api/v1/"),
        };
        sharedClient.DefaultRequestHeaders.Add("X-Finnhub-Token", "the-apps-own-key");

        var provider = new FinnhubQuoteProvider(
            sharedClient,
            new StaticHttpClientFactory(byokClient),
            new FakeTimeProvider(Now),
            NullLogger<FinnhubQuoteProvider>.Instance);

        await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            "a-users-own-key",
            TestContext.Current.CancellationToken);

        headerHandler.LastTokenHeader.ShouldBe("a-users-own-key");
    }

    /// <summary>With no override, the byok client is never even asked for — the app's own key path is untouched.</summary>
    [Fact]
    public async Task GetQuotes_WithNoApiKeyOverride_NeverAsksTheFactoryForTheByokClient()
    {
        var sharedHandler = new CountingHandler(HttpStatusCode.OK, """{"c":1.23,"t":1000000000}""");
        var sharedClient = new HttpClient(sharedHandler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") };

        var factory = new StaticHttpClientFactory(new HttpClient(new CountingHandler(HttpStatusCode.OK)));
        var provider = new FinnhubQuoteProvider(sharedClient, factory, new FakeTimeProvider(Now), NullLogger<FinnhubQuoteProvider>.Instance);

        await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            apiKeyOverride: null,
            TestContext.Current.CancellationToken);

        sharedHandler.Calls.ShouldBe(1);
        factory.RequestedName.ShouldBeNull();
    }

    [Fact]
    public async Task VerifyKey_WhenTheProviderAccepts_IsAccepted()
    {
        var provider = Build(new CountingHandler(HttpStatusCode.OK, """{"count":1,"result":[{"symbol":"AAPL"}]}"""));

        var verdict = await provider.VerifyKeyAsync("a-candidate-key", TestContext.Current.CancellationToken);

        verdict.ShouldBe(KeyVerdict.Accepted);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task VerifyKey_WhenTheProviderRejects_IsRejectedNotUnknown(HttpStatusCode status)
    {
        var provider = Build(new CountingHandler(status));

        var verdict = await provider.VerifyKeyAsync("a-candidate-key", TestContext.Current.CancellationToken);

        verdict.ShouldBe(KeyVerdict.Rejected);
    }

    /// <summary>The check this test exists for: an unanswerable provider must never be read as a bad key.</summary>
    [Fact]
    public async Task VerifyKey_WhenTheProviderCannotAnswer_IsUnknownNotRejected()
    {
        var provider = Build(new CountingHandler(HttpStatusCode.InternalServerError));

        var verdict = await provider.VerifyKeyAsync("a-candidate-key", TestContext.Current.CancellationToken);

        verdict.ShouldBe(KeyVerdict.Unknown);
    }

    [Fact]
    public async Task VerifyKey_WhenTheTransportFails_IsUnknown()
    {
        var provider = BuildWithFlaky(new FlakyHandler("AAPL"));

        var verdict = await provider.VerifyKeyAsync("a-candidate-key", TestContext.Current.CancellationToken);

        verdict.ShouldBe(KeyVerdict.Unknown);
    }

    /// <summary>The app's own key must never leak into a check of someone else's candidate key.</summary>
    [Fact]
    public async Task VerifyKey_SendsTheCandidateKey_NotTheClientsOwn()
    {
        var handler = new HeaderCapturingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") };

        // Mimics MarketDataModule.cs, which adds the app's own key as a default header on this client.
        client.DefaultRequestHeaders.Add("X-Finnhub-Token", "the-apps-own-key");

        var provider = new FinnhubQuoteProvider(
            client,
            new StaticHttpClientFactory(client),
            new FakeTimeProvider(Now),
            NullLogger<FinnhubQuoteProvider>.Instance);

        await provider.VerifyKeyAsync("candidate-key", TestContext.Current.CancellationToken);

        handler.LastTokenHeader.ShouldBe("candidate-key");
    }

    private static FinnhubQuoteProvider BuildWithFlaky(FlakyHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") },
            new StaticHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") }),
            new FakeTimeProvider(Now),
            NullLogger<FinnhubQuoteProvider>.Instance);

    /// <summary>Always hands back the one client it was built with, and records the name it was asked for.</summary>
    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public string? RequestedName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            RequestedName = name;

            return client;
        }
    }

    private sealed class HeaderCapturingHandler : HttpMessageHandler
    {
        public string? LastTokenHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastTokenHeader = request.Headers.TryGetValues("X-Finnhub-Token", out var values)
                ? values.FirstOrDefault()
                : null;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"count":1,"result":[{"symbol":"AAPL"}]}""", System.Text.Encoding.UTF8, "application/json"),
            });
        }
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
