using System.Net;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

namespace StockPortfolio.Tests;

public sealed class FinnhubQuoteProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 15, 0, 0, TimeSpan.Zero);

    private static FinnhubQuoteProvider Build(CountingHandler handler, ProviderKeyRejection? rejection = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") },
            new StaticHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") }),
            new FakeTimeProvider(Now),
            rejection ?? new ProviderKeyRejection(),
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

    // The fake never produces UnknownTicker, so the real mapping is asserted nowhere else.
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

        matches.ShouldHaveSingleItem().Ticker.Value.ShouldBe("AAPL");
        matches[0].Name.ShouldBe("APPLE INC");
    }

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

    // A header alone on the shared client would not isolate a per-user key's breaker from the app's own.
    [Fact]
    public async Task GetQuotes_WithAnApiKeyOverride_RoutesThroughTheByokNamedClient()
    {
        var sharedHandler = new CountingHandler(HttpStatusCode.OK, """{"c":1.23,"t":1000000000}""");
        var byokHandler = new CountingHandler(HttpStatusCode.OK, """{"c":4.56,"t":1000000000}""");

        var sharedClient = new HttpClient(sharedHandler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") };
        var byokClient = new HttpClient(byokHandler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") };

        var factory = new StaticHttpClientFactory(byokClient);
        var provider = new FinnhubQuoteProvider(
            sharedClient, factory, new FakeTimeProvider(Now), new ProviderKeyRejection(), NullLogger<FinnhubQuoteProvider>.Instance);

        var quotes = await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            "a-users-own-key",
            TestContext.Current.CancellationToken);

        sharedHandler.Calls.ShouldBe(0, "an override key must never touch the client every dashboard and the poller share");
        byokHandler.Calls.ShouldBe(1);
        quotes.ShouldHaveSingleItem().Price.ShouldBe(4.56m);
        factory.RequestedName.ShouldBe(FinnhubQuoteProvider.ByokClientName);
    }

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
            new ProviderKeyRejection(),
            NullLogger<FinnhubQuoteProvider>.Instance);

        await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            "a-users-own-key",
            TestContext.Current.CancellationToken);

        headerHandler.LastTokenHeader.ShouldBe("a-users-own-key");
    }

    [Fact]
    public async Task GetQuotes_WithNoApiKeyOverride_NeverAsksTheFactoryForTheByokClient()
    {
        var sharedHandler = new CountingHandler(HttpStatusCode.OK, """{"c":1.23,"t":1000000000}""");
        var sharedClient = new HttpClient(sharedHandler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") };

        var factory = new StaticHttpClientFactory(new HttpClient(new CountingHandler(HttpStatusCode.OK)));
        var provider = new FinnhubQuoteProvider(
            sharedClient, factory, new FakeTimeProvider(Now), new ProviderKeyRejection(), NullLogger<FinnhubQuoteProvider>.Instance);

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

    // On the shared client, a good key would be reported unreachable whenever the dashboard's breaker was open.
    [Fact]
    public async Task VerifyKey_RoutesThroughTheByokNamedClient()
    {
        var sharedHandler = new CountingHandler(HttpStatusCode.OK, """{"count":1,"result":[{"symbol":"AAPL"}]}""");
        var byokHandler = new CountingHandler(HttpStatusCode.OK, """{"count":1,"result":[{"symbol":"AAPL"}]}""");

        var sharedClient = new HttpClient(sharedHandler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") };
        var byokClient = new HttpClient(byokHandler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") };

        var factory = new StaticHttpClientFactory(byokClient);
        var provider = new FinnhubQuoteProvider(
            sharedClient, factory, new FakeTimeProvider(Now), new ProviderKeyRejection(), NullLogger<FinnhubQuoteProvider>.Instance);

        var verdict = await provider.VerifyKeyAsync("a-candidate-key", TestContext.Current.CancellationToken);

        verdict.ShouldBe(KeyVerdict.Accepted);
        sharedHandler.Calls.ShouldBe(0, "verifying a candidate key must never touch the client every dashboard and the poller share");
        byokHandler.Calls.ShouldBe(1);
        factory.RequestedName.ShouldBe(FinnhubQuoteProvider.ByokClientName);
    }

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
            new ProviderKeyRejection(),
            NullLogger<FinnhubQuoteProvider>.Instance);

        await provider.VerifyKeyAsync("candidate-key", TestContext.Current.CancellationToken);

        handler.LastTokenHeader.ShouldBe("candidate-key");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Fetch_WhenTheApplicationsOwnKeyIsRefused_RaisesTheRejectedKeyFlag(HttpStatusCode status)
    {
        var rejection = new ProviderKeyRejection();
        var provider = Build(new CountingHandler(status), rejection);

        await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            apiKeyOverride: null,
            TestContext.Current.CancellationToken);

        rejection.IsRejected.ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Fetch_WhenAUsersOwnKeyIsRefused_LeavesTheFlagAlone(HttpStatusCode status)
    {
        var rejection = new ProviderKeyRejection();
        var provider = Build(new CountingHandler(status), rejection);

        await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            "a-users-own-key",
            TestContext.Current.CancellationToken);

        rejection.IsRejected.ShouldBeFalse();
    }

    [Fact]
    public async Task VerifyKey_WhenACandidateKeyIsRejected_LeavesTheFlagAlone()
    {
        var rejection = new ProviderKeyRejection();
        var provider = Build(new CountingHandler(HttpStatusCode.Unauthorized), rejection);

        (await provider.VerifyKeyAsync("a-candidate-key", TestContext.Current.CancellationToken))
            .ShouldBe(KeyVerdict.Rejected);

        rejection.IsRejected.ShouldBeFalse();
    }

    [Fact]
    public async Task Fetch_WhenEverySymbolFails_WarnsOnceWithACountRatherThanOncePerSymbol()
    {
        var logger = new RecordingLogger();

        var provider = new FinnhubQuoteProvider(
            new HttpClient(new AlwaysThrowingHandler()) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") },
            new StaticHttpClientFactory(new HttpClient(new AlwaysThrowingHandler())),
            new FakeTimeProvider(Now),
            new ProviderKeyRejection(),
            logger);

        await provider.GetQuotesAsync(
            new HashSet<Ticker>
            {
                Ticker.Create("AAPL").AsT0,
                Ticker.Create("MSFT").AsT0,
                Ticker.Create("TSLA").AsT0,
            },
            apiKeyOverride: null,
            TestContext.Current.CancellationToken);

        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();

        warnings.ShouldHaveSingleItem().Message.ShouldContain("3 of 3");

        logger.Entries.Count(entry => entry.Level == LogLevel.Debug).ShouldBe(3);
    }

    [Fact]
    public async Task Fetch_WhenTheBodyIsNotTheExpectedShape_LogsTheRawBodyAtDebugOnly()
    {
        var logger = new RecordingLogger();

        var provider = new FinnhubQuoteProvider(
            new HttpClient(new CountingHandler(HttpStatusCode.OK, "<html>Access denied by the firewall</html>", "text/html"))
            {
                BaseAddress = new Uri("https://api.finnhub.io/api/v1/"),
            },
            new StaticHttpClientFactory(new HttpClient(new CountingHandler(HttpStatusCode.OK))),
            new FakeTimeProvider(Now),
            new ProviderKeyRejection(),
            logger);

        await provider.GetQuotesAsync(
            new HashSet<Ticker> { Ticker.Create("AAPL").AsT0 },
            apiKeyOverride: null,
            TestContext.Current.CancellationToken);

        logger.Entries
            .Where(entry => entry.Level == LogLevel.Debug)
            .ShouldContain(entry => entry.Message.Contains("Access denied by the firewall", StringComparison.Ordinal));

        logger.Entries
            .Where(entry => entry.Level >= LogLevel.Information)
            .ShouldAllBe(entry => !entry.Message.Contains("Access denied by the firewall", StringComparison.Ordinal));
    }

    private static FinnhubQuoteProvider BuildWithFlaky(FlakyHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") },
            new StaticHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://api.finnhub.io/api/v1/") }),
            new FakeTimeProvider(Now),
            new ProviderKeyRejection(),
            NullLogger<FinnhubQuoteProvider>.Instance);

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

    private sealed class AlwaysThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new HttpRequestException("upstream refused");
    }

    private sealed class RecordingLogger : ILogger<FinnhubQuoteProvider>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Entries.Add((logLevel, formatter(state, exception)));
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
