using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Application.Prices;
using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Prices;

namespace StockPortfolio.Tests;

public sealed class QuoteReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    private static Ticker T(string value) => Ticker.Create(value).AsT0;

    private static QuoteReader Build(IQuoteProvider provider, ILastKnownPriceStore store) =>
        new(provider, store, new FakeTimeProvider(Now));

    [Fact]
    public async Task Fetch_WritesLastKnownPrice()
    {
        var store = new RecordingStore();
        var reader = Build(new StubProvider(new Quote(T("AAPL"), 187.42m, Now)), store);

        await reader.GetCurrentPricesAsync(["AAPL"], TestContext.Current.CancellationToken);

        // One writer in QuoteReader, so the fake and Finnhub paths record identically.
        store.Written.ShouldHaveSingleItem().Price.ShouldBe(187.42m);
    }

    [Fact]
    public async Task Fetch_PartialProviderFailure_MixesFreshAndLastKnown()
    {
        var store = new RecordingStore();
        store.Stored[T("MSFT")] = new LastPrice(410.00m, Now - TimeSpan.FromMinutes(30));

        var reader = Build(
            new StubProvider(new Quote(T("AAPL"), 187.42m, Now), new Quote(T("TSLA"), 250.00m, Now)),
            store);

        var prices = await reader.GetCurrentPricesAsync(
            ["AAPL", "MSFT", "TSLA"],
            TestContext.Current.CancellationToken);

        // Seventeen-of-twenty in miniature: one failure must not discard the two that succeeded.
        prices.Count.ShouldBe(3);
        prices["AAPL"].IsLastKnown.ShouldBeFalse();
        prices["TSLA"].IsLastKnown.ShouldBeFalse();
        prices["MSFT"].IsLastKnown.ShouldBeTrue();
        prices["MSFT"].ObservedAt.ShouldBe(Now - TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task Fetch_NeverFetchedAndProviderDown_IsAbsentNotZero()
    {
        var prices = await Build(new StubProvider(), new RecordingStore())
            .GetCurrentPricesAsync(["AAPL"], TestContext.Current.CancellationToken);

        prices.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fetch_CorruptStoredPrice_IsNotShown()
    {
        var store = new RecordingStore();
        store.Stored[T("AAPL")] = new LastPrice(0m, Now - TimeSpan.FromMinutes(1));

        var prices = await Build(new StubProvider(), store)
            .GetCurrentPricesAsync(["AAPL"], TestContext.Current.CancellationToken);

        prices.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fetch_KeysAreCanonicalUpperCaseAndOrdinal()
    {
        var prices = await Build(new StubProvider(new Quote(T("AAPL"), 187.42m, Now)), new RecordingStore())
            .GetCurrentPricesAsync(["aapl", "  AAPL  ", "BRK.B"], TestContext.Current.CancellationToken);

        // Both sides canonicalise, so Ordinal turns a divergence into a visible miss instead of hiding it.
        prices.Keys.ShouldBe(["AAPL"]);
        prices.ContainsKey("aapl").ShouldBeFalse();
    }

    [Fact]
    public async Task Fetch_RedisUnreachable_StillReturnsThePrice()
    {
        var options = ConfigurationOptions.Parse("127.0.0.1:1");
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 50;
        options.ConnectRetry = 1;
        options.SyncTimeout = 50;

        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);

        var reader = Build(
            new StubProvider(new Quote(T("AAPL"), 187.42m, Now)),
            new RedisLastKnownPriceStore(multiplexer, NullLogger<RedisLastKnownPriceStore>.Instance));

        var prices = await reader.GetCurrentPricesAsync(["AAPL"], TestContext.Current.CancellationToken);

        prices["AAPL"].Price.ShouldBe(187.42m);
        prices["AAPL"].IsLastKnown.ShouldBeFalse();
    }

    [Theory]
    [InlineData("aapl", true)]
    [InlineData("BRK.B", false)]
    [InlineData("", false)]
    public async Task SymbolValidator_ChecksShapeThenAsksTheProvider(string candidate, bool expected) =>
        (await new SymbolValidator(new StubProvider()).IsKnownSymbolAsync(
            candidate, TestContext.Current.CancellationToken)).ShouldBe(expected);

    private sealed class StubProvider(params Quote[] quotes) : IQuoteProvider
    {
        public string Name => "Stub";

        public Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlySet<Ticker> tickers, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Quote>>([.. quotes.Where(quote => tickers.Contains(quote.Ticker))]);

        public Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SymbolMatch>>([]);

        public Task<KeyVerdict> VerifyKeyAsync(string apiKey, CancellationToken ct) =>
            Task.FromResult(KeyVerdict.Accepted);
    }

    private sealed class RecordingStore : ILastKnownPriceStore
    {
        public List<Quote> Written { get; } = [];

        public Dictionary<Ticker, LastPrice> Stored { get; } = [];

        public Task<IReadOnlyDictionary<Ticker, LastPrice>> ReadAsync(
            IReadOnlyCollection<Ticker> tickers,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Ticker, LastPrice>>(
                tickers.Where(Stored.ContainsKey).ToDictionary(ticker => ticker, ticker => Stored[ticker]));

        public Task WriteAsync(IReadOnlyCollection<Quote> quotes, CancellationToken ct)
        {
            Written.AddRange(quotes);

            return Task.CompletedTask;
        }
    }
}
