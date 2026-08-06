using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Polling;

namespace StockPortfolio.Tests;

public sealed class QuotePollerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 14, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cycle_WithNoPollTargets_NeverReachesTheProviderAndStoresNothing()
    {
        // The phase's stated exit condition: with nobody holding an alert, the poller costs one Redis SET,
        // one list read and nothing else. A provider call here is a rate-limit budget spent on no user, and
        // a window write here is a series nobody asked to keep.
        using var harness = new Harness(new StubSource());

        await harness.Poller.RunCycleAsync(Ct);

        harness.Provider.Calls.ShouldBe(0);
        harness.Window.Appended.ShouldBeEmpty();
        harness.LastKnown.Written.ShouldBeEmpty();
        harness.Observer.Notified.ShouldBeEmpty();

        // Still released: an early return that keeps the in-flight flag stops the next cycle for five
        // intervals, and the symptom is "polling stopped" with nothing in the log.
        harness.Lease.Releases.ShouldBe(1);
    }

    [Fact]
    public async Task Cycle_LeaseRefused_DoesNotEvenAskWhatToPoll()
    {
        var source = new StubSource("AAPL");

        using var harness = new Harness(source) { Lease = { Grants = false } };

        await harness.Poller.RunCycleAsync(Ct);

        source.Calls.ShouldBe(0);
        harness.Provider.Calls.ShouldBe(0);

        // Nothing was taken, so nothing may be given back: deleting the in-flight key this replica does not
        // hold is exactly the overlap the second lock exists to prevent.
        harness.Lease.Releases.ShouldBe(0);
    }

    [Fact]
    public async Task Cycle_EverySample_LandsInBothTheWindowAndTheLastKnownPrice()
    {
        using var harness = new Harness(
            new StubSource("AAPL", "MSFT"),
            new Quote(T("AAPL"), 187.42m, Now),
            new Quote(T("MSFT"), 410.00m, Now));

        await harness.Poller.RunCycleAsync(Ct);

        // Both, from one place. The window is what alerts read and the last-known key is the dashboard's
        // fallback; a sample in one and not the other is two features disagreeing about the same fetch.
        harness.Window.Appended.Select(sample => sample.Ticker).ShouldBe(["AAPL", "MSFT"], ignoreOrder: true);
        harness.Window.Appended.ShouldAllBe(sample => sample.Retention == TimeSpan.FromMinutes(75));
        harness.LastKnown.Written.Select(quote => quote.Ticker.Value).ShouldBe(["AAPL", "MSFT"], ignoreOrder: true);
        harness.Observer.Notified.ShouldBe(["AAPL", "MSFT"], ignoreOrder: true);
        harness.Lease.Releases.ShouldBe(1);
    }

    [Fact]
    public async Task Cycle_TargetsThatAreNotTickers_AreDroppedBeforeTheProvider()
    {
        using var harness = new Harness(
            new StubSource("aapl", "  AAPL ", "TOOLONG", "", "BRK.B"),
            new Quote(T("AAPL"), 187.42m, Now));

        await harness.Poller.RunCycleAsync(Ct);

        harness.Provider.Requested.ShouldHaveSingleItem().ShouldBe(T("AAPL"));
    }

    [Fact]
    public async Task Cycle_ProviderThrows_StillHandsTheInFlightLeaseBack()
    {
        using var harness = new Harness(new StubSource("AAPL")) { Provider = { Throws = true } };

        await Should.ThrowAsync<InvalidOperationException>(() => harness.Poller.RunCycleAsync(Ct));

        // The release is in a finally, not on the happy path. Without it a crashed cycle blocks polling
        // until the key expires, and only on the replica that failed.
        harness.Lease.Releases.ShouldBe(1);
    }

    [Fact]
    public async Task Cycle_ObserverThrowsOnOneTicker_TheNextIsStillSampled()
    {
        using var harness = new Harness(
            new StubSource("AAPL", "MSFT"),
            new Quote(T("AAPL"), 187.42m, Now),
            new Quote(T("MSFT"), 410.00m, Now));

        harness.Observer.ThrowOn = "AAPL";

        await harness.Poller.RunCycleAsync(Ct);

        // The abstraction's doc comment says a failed observer must not stop the next ticker being sampled.
        // Only the loop can make that true, so the loop is what this asserts.
        harness.Observer.Notified.ShouldBe(["AAPL", "MSFT"], ignoreOrder: true);
        harness.Window.Appended.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Loop_ACycleThrows_TheServiceSurvivesAndTheNextCycleRuns()
    {
        var clock = new FakeTimeProvider(Now);
        var source = new SequencedSource();
        using var harness = new Harness(source, clock);

        await harness.Poller.StartAsync(Ct);

        // Nudged rather than counted. PeriodicTimer does not buffer a tick that arrives before the service
        // has registered its wait, and neither that moment nor the end of a cycle is observable from out
        // here — a single Advance right after StartAsync races both and loses.
        var deadline = DateTimeOffset.UtcNow + Patience;

        while (source.Cycles < 2 && DateTimeOffset.UtcNow < deadline)
        {
            clock.Advance(Interval);

            await Task.Delay(TimeSpan.FromMilliseconds(20), Ct);
        }

        await harness.Poller.StopAsync(Ct);

        // A second cycle out of a service whose first cycle threw. With the try/catch around the loop
        // instead of inside it, ExecuteAsync would have ended on the first throw — and because StopHost is
        // the default BackgroundServiceExceptionBehavior, the whole host would have gone down with it.
        source.FirstCycleThrew.ShouldBeTrue();
        source.Cycles.ShouldBeGreaterThanOrEqualTo(2);
        harness.Lease.Releases.ShouldBe(source.Cycles);
    }

    private static Ticker T(string value) => Ticker.Create(value).AsT0;

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider services;

        public Harness(IPollTargetSource source, params Quote[] quotes)
            : this(source, new FakeTimeProvider(Now), quotes)
        {
        }

        public Harness(IPollTargetSource source, FakeTimeProvider clock, params Quote[] quotes)
        {
            Provider = new CountingProvider(quotes);

            var collection = new ServiceCollection();

            collection.AddScoped(_ => source);
            collection.AddScoped<IQuoteProvider>(_ => Provider);
            collection.AddScoped<IPriceSampleObserver>(_ => Observer);

            services = collection.BuildServiceProvider();

            Poller = new QuotePoller(
                services.GetRequiredService<IServiceScopeFactory>(),
                Lease,
                Window,
                LastKnown,
                PollingOptions.FromConfiguration(new ConfigurationBuilder().Build()),
                clock,
                NullLogger<QuotePoller>.Instance);
        }

        public QuotePoller Poller { get; }

        public CountingProvider Provider { get; }

        public StubLease Lease { get; } = new();

        public RecordingWindowStore Window { get; } = new();

        public RecordingLastKnownStore LastKnown { get; } = new();

        public RecordingObserver Observer { get; } = new();

        public void Dispose()
        {
            Poller.Dispose();
            services.Dispose();
        }
    }

    private sealed class StubSource(params string[] targets) : IPollTargetSource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> GetPollTargetsAsync(CancellationToken ct)
        {
            Calls++;

            return Task.FromResult<IReadOnlyList<string>>(targets);
        }
    }

    private sealed class SequencedSource : IPollTargetSource
    {
        private int cycles;

        public int Cycles => Volatile.Read(ref cycles);

        public bool FirstCycleThrew { get; private set; }

        public Task<IReadOnlyList<string>> GetPollTargetsAsync(CancellationToken ct)
        {
            if (Interlocked.Increment(ref cycles) != 1)
            {
                return Task.FromResult<IReadOnlyList<string>>([]);
            }

            FirstCycleThrew = true;

            throw new InvalidOperationException("the poll-target adapter was unreachable");
        }
    }

    private sealed class StubLease : IPollLease
    {
        public bool Grants { get; set; } = true;

        public int Releases { get; private set; }

        public Task<bool> TryAcquireAsync(DateTimeOffset now, CancellationToken ct) => Task.FromResult(Grants);

        public Task ReleaseAsync(CancellationToken ct)
        {
            Releases++;

            return Task.CompletedTask;
        }
    }

    private sealed class CountingProvider(params Quote[] quotes) : IQuoteProvider
    {
        public string Name => "Counting";

        public bool Throws { get; set; }

        public int Calls { get; private set; }

        public List<Ticker> Requested { get; } = [];

        public Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlySet<Ticker> tickers, CancellationToken ct)
        {
            Calls++;
            Requested.AddRange(tickers);

            return Throws
                ? throw new InvalidOperationException("the provider fell over")
                : Task.FromResult<IReadOnlyList<Quote>>([.. quotes.Where(quote => tickers.Contains(quote.Ticker))]);
        }

        public Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SymbolMatch>>([]);
    }

    private sealed class RecordingWindowStore : IPriceWindowStore
    {
        public List<(string Ticker, decimal Price, DateTimeOffset At, TimeSpan Retention)> Appended { get; } = [];

        public Task AppendAsync(
            string ticker,
            decimal price,
            DateTimeOffset at,
            TimeSpan retention,
            CancellationToken ct)
        {
            Appended.Add((ticker, price, at, retention));

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<(DateTimeOffset At, decimal Price)>> ReadAsync(
            string ticker,
            DateTimeOffset since,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<(DateTimeOffset At, decimal Price)>>([]);
    }

    private sealed class RecordingLastKnownStore : ILastKnownPriceStore
    {
        public List<Quote> Written { get; } = [];

        public Task<IReadOnlyDictionary<Ticker, LastPrice>> ReadAsync(
            IReadOnlyCollection<Ticker> tickers,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Ticker, LastPrice>>(new Dictionary<Ticker, LastPrice>());

        public Task WriteAsync(IReadOnlyCollection<Quote> quotes, CancellationToken ct)
        {
            Written.AddRange(quotes);

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingObserver : IPriceSampleObserver
    {
        public List<string> Notified { get; } = [];

        public string? ThrowOn { get; set; }

        public Task OnSampleStoredAsync(string ticker, CancellationToken ct)
        {
            Notified.Add(ticker);

            return string.Equals(ticker, ThrowOn, StringComparison.Ordinal)
                ? throw new InvalidOperationException("evaluation blew up")
                : Task.CompletedTask;
        }
    }
}
