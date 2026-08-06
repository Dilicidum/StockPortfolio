using Microsoft.Extensions.Time.Testing;
using Shouldly;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Application.Prices;
using StockPortfolio.Modules.MarketData.Contracts;

namespace StockPortfolio.Tests;

public sealed class PriceWindowReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Window_OverASeries_ReducesToTheFiveNumbersTheRulesRead()
    {
        var store = new RecordingWindowStore(
            (Now.AddMinutes(-60), 150m),
            (Now.AddMinutes(-40), 141m),
            (Now.AddMinutes(-20), 152m),
            (Now, 149m));

        var window = await Read(store, TimeSpan.FromMinutes(60));

        window.ShouldNotBeNull();
        window.Ticker.ShouldBe("AAPL");
        window.Current.ShouldBe(149m);
        window.Oldest.ShouldBe(150m);
        window.Low.ShouldBe(141m);
        window.High.ShouldBe(152m);
        window.OldestAt.ShouldBe(Now.AddMinutes(-60));
        window.NewestAt.ShouldBe(Now);
        window.SampleCount.ShouldBe(4);
    }

    [Fact]
    public async Task Window_Current_IsTheNewestSampleNotTheExtreme()
    {
        // The one that separates "what is it now" from "how far did it go": Current must be the last
        // sample even when the series ends below its own low-water reading order.
        var store = new RecordingWindowStore(
            (Now.AddMinutes(-30), 100m),
            (Now.AddMinutes(-20), 180m),
            (Now.AddMinutes(-10), 90m));

        var window = await Read(store, TimeSpan.FromMinutes(60));

        window.ShouldNotBeNull();
        window.Current.ShouldBe(90m);
        window.Oldest.ShouldBe(100m);
        window.High.ShouldBe(180m);
        window.Low.ShouldBe(90m);
    }

    [Fact]
    public async Task Window_LargestGap_IsTheWidestIntervalNotTheLastOne()
    {
        // The gap guard of the plan reads this field to reject a window straddling a closed market. A
        // reader that reported only the most recent interval would wave a Friday-to-Monday window through.
        var store = new RecordingWindowStore(
            (Now.AddMinutes(-60), 150m),
            (Now.AddMinutes(-55), 151m),
            (Now.AddMinutes(-5), 149m),
            (Now.AddMinutes(-4), 148m));

        var window = await Read(store, TimeSpan.FromMinutes(60));

        window.ShouldNotBeNull();
        window.LargestGap.ShouldBe(TimeSpan.FromMinutes(50));
    }

    [Fact]
    public async Task Window_OneSample_HasNoGap()
    {
        var store = new RecordingWindowStore((Now, 150m));

        var window = await Read(store, TimeSpan.FromMinutes(60));

        window.ShouldNotBeNull();
        window.SampleCount.ShouldBe(1);
        window.LargestGap.ShouldBe(TimeSpan.Zero);
        window.Current.ShouldBe(window.Oldest);
    }

    [Fact]
    public async Task Window_EmptySeries_IsAbsentNotAWindowOfZeroes()
    {
        // A zero-filled window reads as a 100% crash to every rule that consumes it.
        var window = await Read(new RecordingWindowStore(), TimeSpan.FromMinutes(60));

        window.ShouldBeNull();
    }

    [Fact]
    public async Task Window_AsksTheStoreForTheCanonicalTickerAndTheWindowStart()
    {
        var store = new RecordingWindowStore((Now, 150m));

        _ = await Read(store, TimeSpan.FromMinutes(45), ticker: " aapl ");

        store.Ticker.ShouldBe("AAPL");
        store.Since.ShouldBe(Now.AddMinutes(-45));
    }

    [Theory]
    [InlineData("")]
    [InlineData("TOOLONG")]
    [InlineData("AA1")]
    public async Task Window_UnusableTicker_IsAbsentAndNeverReachesTheStore(string ticker)
    {
        var store = new RecordingWindowStore((Now, 150m));

        var window = await Read(store, TimeSpan.FromMinutes(60), ticker);

        window.ShouldBeNull();
        store.Reads.ShouldBe(0);
    }

    [Fact]
    public async Task Window_NonPositiveWindow_IsAbsentAndNeverReachesTheStore()
    {
        var store = new RecordingWindowStore((Now, 150m));

        var window = await Read(store, TimeSpan.Zero);

        window.ShouldBeNull();
        store.Reads.ShouldBe(0);
    }

    private static Task<PriceWindow?> Read(
        RecordingWindowStore store,
        TimeSpan window,
        string ticker = "AAPL")
    {
        var clock = new FakeTimeProvider(Now);

        return new PriceWindowReader(store, clock)
            .GetWindowAsync(ticker, window, TestContext.Current.CancellationToken);
    }

    private sealed class RecordingWindowStore(params (DateTimeOffset At, decimal Price)[] samples)
        : IPriceWindowStore
    {
        public int Reads { get; private set; }

        public string? Ticker { get; private set; }

        public DateTimeOffset Since { get; private set; }

        public Task AppendAsync(
            string ticker,
            decimal price,
            DateTimeOffset at,
            TimeSpan retention,
            CancellationToken ct) => Task.CompletedTask;

        public Task<IReadOnlyList<(DateTimeOffset At, decimal Price)>> ReadAsync(
            string ticker,
            DateTimeOffset since,
            CancellationToken ct)
        {
            Reads++;
            Ticker = ticker;
            Since = since;

            return Task.FromResult<IReadOnlyList<(DateTimeOffset At, decimal Price)>>(samples);
        }
    }
}
