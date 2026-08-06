using Shouldly;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Application.Names;
using StockPortfolio.Modules.MarketData.Application.Tickers.Queries.SearchTickers;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Tests;

public sealed class SearchTickersQueryHandlerTests
{
    private static Ticker T(string value) => Ticker.Create(value).AsT0;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData(" a ")]
    public async Task Search_QueryTooShort_AsksTheProviderNothing(string? query)
    {
        var provider = new RecordingProvider();
        var names = new RecordingStore();

        var results = await Build(provider, names).Handle(
            new SearchTickersQuery(query),
            TestContext.Current.CancellationToken);

        results.ShouldBeEmpty();

        // Every keystroke reaches this handler, so a one-letter query costing a provider call would
        // spend the whole rate-limit budget on typing.
        provider.Queries.ShouldBeEmpty();
        names.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_TrimsBeforeAsking()
    {
        var provider = new RecordingProvider(new SymbolMatch(T("AAPL"), "Apple Inc"));

        _ = await Build(provider, new RecordingStore()).Handle(
            new SearchTickersQuery("  appl  "),
            TestContext.Current.CancellationToken);

        provider.Queries.ShouldHaveSingleItem().ShouldBe("appl");
    }

    /// <summary>Every match is cached, not only the suggested ones: it warms the symbol about to be picked.</summary>
    [Fact]
    public async Task Search_CachesEveryMatch_NotJustTheOnesItSuggests()
    {
        var matches = Enumerable.Range(0, 30)
            .Select(index => new SymbolMatch(T(Symbol(index)), "Company " + index))
            .ToArray();

        var provider = new RecordingProvider(matches);
        var names = new RecordingStore();

        var results = await Build(provider, names).Handle(
            new SearchTickersQuery("co"),
            TestContext.Current.CancellationToken);

        results.Count.ShouldBe(
            SearchTickersQueryHandler.MaximumSuggestions,
            "a dropdown is not a directory listing, so the response is capped");

        names.Written.Count.ShouldBe(
            30,
            "the cache learns everything the search saw — that is the cheap half, and it is the half "
            + "that makes the holdings page able to name a symbol nobody has looked at yet");
    }

    [Fact]
    public async Task Search_ProviderAnswersNothing_WritesNothingAndReturnsNothing()
    {
        var names = new RecordingStore();

        var results = await Build(new RecordingProvider(), names).Handle(
            new SearchTickersQuery("zzzz"),
            TestContext.Current.CancellationToken);

        results.ShouldBeEmpty();
        names.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_CarriesSymbolAndDescription()
    {
        var provider = new RecordingProvider(new SymbolMatch(T("AAPL"), "Apple Inc"));

        var results = await Build(provider, new RecordingStore()).Handle(
            new SearchTickersQuery("appl"),
            TestContext.Current.CancellationToken);

        var only = results.ShouldHaveSingleItem();

        only.Symbol.ShouldBe("AAPL");
        only.Description.ShouldBe("Apple Inc");
    }

    /// <summary>The read side of the same store, and the reason the holdings page never calls a provider.</summary>
    [Fact]
    public async Task NameReader_ReturnsOnlyWhatIsCached_KeyedCanonically()
    {
        var store = new RecordingStore();

        await store.WriteAsync(
            [new SymbolMatch(T("AAPL"), "Apple Inc")],
            TestContext.Current.CancellationToken);

        var names = await new CompanyNameReader(store).GetNamesAsync(
            ["aapl", "  AAPL  ", "MSFT", "BRK.B", ""],
            TestContext.Current.CancellationToken);

        // One key, canonical and Ordinal: both sides canonicalise, so a divergence is a visible miss.
        names.Keys.ShouldBe(["AAPL"]);
        names["AAPL"].ShouldBe("Apple Inc");
        names.ContainsKey("aapl").ShouldBeFalse();
    }

    [Fact]
    public async Task NameReader_NoWellShapedTickers_ReadsNothing()
    {
        var store = new RecordingStore();

        (await new CompanyNameReader(store).GetNamesAsync(
            ["BRK.B", ""],
            TestContext.Current.CancellationToken)).ShouldBeEmpty();

        store.Reads.ShouldBe(0, "a page of unparseable symbols must not cost a Redis round trip");
    }

    private static SearchTickersQueryHandler Build(IQuoteProvider provider, ICompanyNameStore names) =>
        new(provider, names);

    /// <summary>AAA..DD, so thirty distinct symbols all fit the 1-to-5-letter shape.</summary>
    private static string Symbol(int index) =>
        new([(char)('A' + (index / 26)), (char)('A' + (index % 26))]);

    private sealed class RecordingProvider(params SymbolMatch[] matches) : IQuoteProvider
    {
        public string Name => "Recording";

        public List<string> Queries { get; } = [];

        public Task<IReadOnlyList<Quote>> GetQuotesAsync(
            IReadOnlySet<Ticker> tickers, string? apiKeyOverride, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Quote>>([]);

        public Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct) => Task.FromResult(true);

        public Task<KeyVerdict> VerifyKeyAsync(string apiKey, CancellationToken ct) =>
            Task.FromResult(KeyVerdict.Accepted);

        public Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct)
        {
            Queries.Add(query);

            return Task.FromResult<IReadOnlyList<SymbolMatch>>(matches);
        }
    }

    private sealed class RecordingStore : ICompanyNameStore
    {
        public List<SymbolMatch> Written { get; } = [];

        public int Reads { get; private set; }

        public Task<IReadOnlyDictionary<Ticker, string>> ReadAsync(
            IReadOnlyCollection<Ticker> tickers,
            CancellationToken ct)
        {
            Reads++;

            return Task.FromResult<IReadOnlyDictionary<Ticker, string>>(
                Written
                    .Where(match => tickers.Contains(match.Ticker))
                    .ToDictionary(match => match.Ticker, match => match.Name));
        }

        public Task WriteAsync(IReadOnlyCollection<SymbolMatch> matches, CancellationToken ct)
        {
            Written.AddRange(matches);

            return Task.CompletedTask;
        }
    }
}
