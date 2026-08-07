using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StackExchange.Redis;

using StockPortfolio.Modules.MarketData.Application;
using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Application.Prices;
using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Prices;
using StockPortfolio.Tests.Fakes;

namespace StockPortfolio.Tests;

public sealed class QuoteReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid AUser = Guid.CreateVersion7();

    private static Ticker T(string value) => Ticker.Create(value).AsT0;

    private static QuoteReader Build(
        IQuoteProvider provider,
        ILastKnownPriceStore store,
        IUserProviderKeyReader? keyReader = null,
        IUserProviderKeyRepository? keyRepository = null,
        ByokOptions? byokOptions = null) =>
        new(
            provider,
            store,
            keyReader ?? new StubKeyReader(_ => null),
            keyRepository ?? new FakeUserProviderKeyRepository(),
            byokOptions ?? new ByokOptions(true),
            new FakeTimeProvider(Now));

    [Fact]
    public async Task Fetch_WritesLastKnownPrice()
    {
        var store = new RecordingStore();
        var reader = Build(new StubProvider(new Quote(T("AAPL"), 187.42m, Now)), store);

        await reader.GetCurrentPricesAsync(AUser, ["AAPL"], TestContext.Current.CancellationToken);

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
            AUser,
            ["AAPL", "MSFT", "TSLA"],
            TestContext.Current.CancellationToken);

        // One failure must not discard the two that succeeded — IsLastKnown is what separates a served price from a fallback.
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
            .GetCurrentPricesAsync(AUser, ["AAPL"], TestContext.Current.CancellationToken);

        prices.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fetch_CorruptStoredPrice_IsNotShown()
    {
        var store = new RecordingStore();
        store.Stored[T("AAPL")] = new LastPrice(0m, Now - TimeSpan.FromMinutes(1));

        var prices = await Build(new StubProvider(), store)
            .GetCurrentPricesAsync(AUser, ["AAPL"], TestContext.Current.CancellationToken);

        prices.ShouldBeEmpty();
    }

    [Fact]
    public async Task Fetch_KeysAreCanonicalUpperCaseAndOrdinal()
    {
        var prices = await Build(new StubProvider(new Quote(T("AAPL"), 187.42m, Now)), new RecordingStore())
            .GetCurrentPricesAsync(AUser, ["aapl", "  AAPL  ", "BRK.B"], TestContext.Current.CancellationToken);

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

        var prices = await reader.GetCurrentPricesAsync(AUser, ["AAPL"], TestContext.Current.CancellationToken);

        prices["AAPL"].Price.ShouldBe(187.42m);
        prices["AAPL"].IsLastKnown.ShouldBeFalse();
    }

    [Fact]
    public async Task GetCurrentPrices_WhenTheUserHasTheirOwnKey_PassesItToTheProvider()
    {
        var provider = new StubProvider(new Quote(T("AAPL"), 187.42m, Now));
        var reader = Build(provider, new RecordingStore(), new StubKeyReader(_ => "user-key-a1b2"));

        await reader.GetCurrentPricesAsync(AUser, ["AAPL"], TestContext.Current.CancellationToken);

        provider.LastApiKeyOverride.ShouldBe("user-key-a1b2");
    }

    [Fact]
    public async Task GetCurrentPrices_WhenTheUserHasNoKey_LeavesTheProviderOnTheApplicationKey()
    {
        var provider = new StubProvider(new Quote(T("AAPL"), 187.42m, Now));
        var reader = Build(provider, new RecordingStore(), new StubKeyReader(_ => null));

        await reader.GetCurrentPricesAsync(AUser, ["AAPL"], TestContext.Current.CancellationToken);

        provider.LastApiKeyOverride.ShouldBeNull();
    }

    [Fact]
    public async Task GetCurrentPrices_WhenByokIsDisabled_NeverReadsTheStoredKey()
    {
        var provider = new StubProvider(new Quote(T("AAPL"), 187.42m, Now));
        var keyReader = new StubKeyReader(_ => throw new InvalidOperationException(
            "The stored key must not be read while BYOK is disabled."));
        var reader = Build(provider, new RecordingStore(), keyReader, byokOptions: new ByokOptions(false));

        await reader.GetCurrentPricesAsync(AUser, ["AAPL"], TestContext.Current.CancellationToken);

        provider.LastApiKeyOverride.ShouldBeNull();
    }

    [Fact]
    public async Task GetCurrentPrices_WhenTheProviderRefusesTheUsersKey_MarksItRejected()
    {
        var repository = new FakeUserProviderKeyRepository();
        repository.Saved.Add(UserProviderKey.Create(AUser, "cipher", "a1b2", TimeProvider.System));

        var provider = new StubProvider { VerifyVerdict = KeyVerdict.Rejected };
        var reader = Build(provider, new RecordingStore(), new StubKeyReader(_ => "user-key-a1b2"), repository);

        await reader.GetCurrentPricesAsync(AUser, ["AAPL"], TestContext.Current.CancellationToken);

        repository.Saved.ShouldHaveSingleItem().LastRejectedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetCurrentPrices_WhenTheProviderCannotAnswerForTheUsersKey_DoesNotMarkItRejected()
    {
        var repository = new FakeUserProviderKeyRepository();
        repository.Saved.Add(UserProviderKey.Create(AUser, "cipher", "a1b2", TimeProvider.System));

        var provider = new StubProvider { VerifyVerdict = KeyVerdict.Unknown };
        var reader = Build(provider, new RecordingStore(), new StubKeyReader(_ => "user-key-a1b2"), repository);

        await reader.GetCurrentPricesAsync(AUser, ["AAPL"], TestContext.Current.CancellationToken);

        repository.Saved.ShouldHaveSingleItem().LastRejectedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("aapl", true)]
    [InlineData("BRK.B", false)]
    [InlineData("", false)]
    public async Task SymbolValidator_ChecksShapeThenAsksTheProvider(string candidate, bool expected) =>
        (await new SymbolValidator(new StubProvider()).IsKnownSymbolAsync(
            candidate, TestContext.Current.CancellationToken)).ShouldBe(expected);

    private sealed class StubKeyReader(Func<Guid, string?> keyFor) : IUserProviderKeyReader
    {
        public Task<string?> ReadPlaintextAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(keyFor(userId));
    }

    private sealed class StubProvider(params Quote[] quotes) : IQuoteProvider
    {
        public string Name => "Stub";

        public string? LastApiKeyOverride { get; private set; }

        public KeyVerdict VerifyVerdict { get; set; } = KeyVerdict.Accepted;

        public Task<IReadOnlyList<Quote>> GetQuotesAsync(
            IReadOnlySet<Ticker> tickers, string? apiKeyOverride, CancellationToken ct)
        {
            LastApiKeyOverride = apiKeyOverride;

            return Task.FromResult<IReadOnlyList<Quote>>([.. quotes.Where(quote => tickers.Contains(quote.Ticker))]);
        }

        public Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SymbolMatch>>([]);

        public Task<KeyVerdict> VerifyKeyAsync(string apiKey, CancellationToken ct) =>
            Task.FromResult(VerifyVerdict);
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
