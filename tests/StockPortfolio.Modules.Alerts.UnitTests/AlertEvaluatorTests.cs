using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using Shouldly;

using StockPortfolio.Modules.Alerts.Application;
using StockPortfolio.Modules.Alerts.Application.Evaluation;
using StockPortfolio.Modules.Alerts.Application.Streaming;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Tests.Fakes;

namespace StockPortfolio.Tests;

/// <summary>The guards, the cooldown and the order of the two writes. No infrastructure anywhere.</summary>
public sealed class AlertEvaluatorTests
{
    private const string Symbol = "AAPL";
    private const int WindowMinutes = 30;
    private const decimal Threshold = 5m;

    /// <summary>The poll interval times the missed-sample allowance: 60s x 3.</summary>
    private static readonly TimeSpan MaxSampleGap = TimeSpan.FromMinutes(3);

    private static readonly DateTimeOffset Now = new(2026, 8, 6, 14, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly List<string> _journal = [];
    private readonly FakeAlertSettingRepository _settings = new();
    private readonly FakePriceWindowReader _windows = new();

    private FakeFiredAlertRepository _firedAlerts = null!;
    private FakeAlertPublisher _publisher = null!;
    private FakeAlertCooldownStore _cooldowns = null!;

    /// <summary>A ticker nobody watches must cost nothing at all — not even one window read.</summary>
    [Fact]
    public async Task ATickerNobodyWatches_ReadsNoWindow()
    {
        await BuildEvaluator().EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _windows.Requested.ShouldBeEmpty(
            "with no enabled threshold on this ticker there is nothing to judge, so a window read is "
                + "a Redis round trip bought for no reason at all.");

        _firedAlerts.Rows.ShouldBeEmpty();
    }

    /// <summary>A move well over the threshold fires once, and the row carries both measurements.</summary>
    [Fact]
    public async Task ABreachOnAWatchedTicker_RecordsAndPublishesOnce()
    {
        var userId = WatchedBy("fall");

        _windows.Returning(WindowMinutes, Falling());

        await BuildEvaluator().EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        var alert = _firedAlerts.Rows.ShouldHaveSingleItem();

        alert.UserId.ShouldBe(userId);
        alert.Ticker.Value.ShouldBe(Symbol);
        alert.Direction.ShouldBe(AlertDirection.Fall);
        alert.ChangePercent.ShouldBeLessThan(-Threshold);
        alert.IsSimulated.ShouldBeFalse();

        // The push describes the saved row, so a live arrival and a refetch cannot disagree.
        _publisher.Sent.ShouldHaveSingleItem().Id.ShouldBe(alert.Id.Value);
    }

    /// <summary>Persist, then publish — and it is the ORDER that matters, not that both happened.</summary>
    [Fact]
    public async Task ABreach_IsSavedBeforeItIsPublished()
    {
        _ = WatchedBy("order");

        _windows.Returning(WindowMinutes, Falling());

        await BuildEvaluator().EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _journal.ShouldBe([FakeFiredAlertRepository.Saved, FakeAlertPublisher.Published]);
    }

    /// <summary>A publisher that throws loses the push and nothing else. The row is what matters.</summary>
    [Fact]
    public async Task APublisherThatThrows_LeavesTheRowSavedAndRaisesNothing()
    {
        _ = WatchedBy("publish-down");

        _windows.Returning(WindowMinutes, Falling());

        await BuildEvaluator(publisherThrows: true)
            .EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _firedAlerts.Rows.ShouldHaveSingleItem(
            "the push is the only thing a publisher failure may cost. Rethrowing here would take the "
                + "whole poll cycle down over a Redis blip and lose every later ticker in it.");
    }

    /// <summary>The same breach evaluated twice inside the cooldown is one alert, not two.</summary>
    [Fact]
    public async Task TheSameBreachTwiceInsideTheCooldown_FiresOnce()
    {
        _ = WatchedBy("cooldown");

        _windows.Returning(WindowMinutes, Falling());

        var evaluator = BuildEvaluator();

        await evaluator.EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _clock.Advance(TimeSpan.FromMinutes(1));

        await evaluator.EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _firedAlerts.Rows.Count.ShouldBe(1);
        _cooldowns.Attempts.ShouldBe(2, "the second cycle must ask, and be told no.");
    }

    /// <summary>Past the cooldown the same breach is news again.</summary>
    [Fact]
    public async Task TheSameBreachAfterTheCooldown_FiresAgain()
    {
        _ = WatchedBy("cooldown-expiry");

        _windows.Returning(WindowMinutes, Falling());

        var evaluator = BuildEvaluator();

        await evaluator.EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _clock.Advance(TimeSpan.FromMinutes(AlertsOptions.DefaultCooldownMinutes + 1));

        // The feed has to have kept up as well: a window whose newest sample is a quarter of an hour
        // old is a stale feed, and the stale guard would suppress this before the cooldown was asked.
        _windows.Returning(WindowMinutes, Falling() with { NewestAt = _clock.GetUtcNow() });

        await evaluator.EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _firedAlerts.Rows.Count.ShouldBe(2);
    }

    /// <summary>A refused claim writes nothing — the cooldown is asked BEFORE the row, not after it.</summary>
    [Fact]
    public async Task ARefusedCooldownClaim_WritesNoRowAndPushesNothing()
    {
        _ = WatchedBy("cooldown-refused");

        _windows.Returning(WindowMinutes, Falling());

        await BuildEvaluator(cooldownRefusesEverything: true)
            .EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _cooldowns.Attempts.ShouldBe(1);
        _firedAlerts.Rows.ShouldBeEmpty(
            "a row written before the claim would be a duplicate in the history list even though the "
                + "push was correctly suppressed — visible only on a reload, which is the worst place.");
        _journal.ShouldBeEmpty();
    }

    /// <summary>Guard one. A window still filling up is the ordinary state, and it judges nothing.</summary>
    [Fact]
    public async Task AWindowWithTooFewSamples_FiresNothing()
    {
        _ = WatchedBy("thin");

        _windows.Returning(WindowMinutes, Falling() with { SampleCount = AlertsOptions.DefaultMinimumSamples - 1 });

        await BuildEvaluator().EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _firedAlerts.Rows.ShouldBeEmpty();
    }

    /// <summary>Guard two. A weekend-shaped gap compares two prices that never faced each other.</summary>
    [Fact]
    public async Task AWindowStraddlingAGap_FiresNothing()
    {
        _ = WatchedBy("gap");

        _windows.Returning(WindowMinutes, Falling() with { LargestGap = MaxSampleGap + TimeSpan.FromSeconds(1) });

        await BuildEvaluator().EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _firedAlerts.Rows.ShouldBeEmpty(
            "the numbers in this window clear the threshold comfortably, so a passing assertion here "
                + "would mean the gap guard is not in the path at all.");
    }

    /// <summary>Guard three, and the one that must not read as "nothing moved": the feed itself stopped.</summary>
    [Fact]
    public async Task AStaleFeed_SuppressesEveryPriceAlertRatherThanReportingCalm()
    {
        _ = WatchedBy("stale");

        // Every number in this window fires. The only thing wrong with it is its age.
        _windows.Returning(WindowMinutes, Falling() with { NewestAt = Now - MaxSampleGap - TimeSpan.FromMinutes(1) });

        await BuildEvaluator().EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _firedAlerts.Rows.ShouldBeEmpty();
        _publisher.Sent.ShouldBeEmpty();
        _cooldowns.Attempts.ShouldBe(
            0,
            "the guard must run before the assessment. Reaching the cooldown at all means a stale feed "
                + "was judged as a price, and the only thing standing between it and an alert was luck.");
    }

    /// <summary>Two users, two window lengths, two reads. One read for the longest would widen the other.</summary>
    [Fact]
    public async Task TwoUsersWithDifferentWindows_AreEachJudgedOverTheirOwn()
    {
        _ = WatchedBy("short", windowMinutes: 5);
        _ = WatchedBy("long", windowMinutes: 60);

        // Calm over five minutes, a slide over sixty. Judged over the longest window alone, the
        // five-minute user is told about an hour-long move they deliberately did not ask about.
        _windows.Returning(5, Calm());
        _windows.Returning(60, Falling());

        await BuildEvaluator().EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _windows.Requested.ShouldBe([TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(60)]);
        _firedAlerts.Rows.ShouldHaveSingleItem().ChangePercent.ShouldBeLessThan(-Threshold);
    }

    /// <summary>A ticker with no series at all — a symbol just added — is silent, not an exception.</summary>
    [Fact]
    public async Task AWatchedTickerWithNoSeries_FiresNothing()
    {
        _ = WatchedBy("no-series");

        await BuildEvaluator().EvaluateAsync(Symbol, TestContext.Current.CancellationToken);

        _windows.Requested.ShouldHaveSingleItem();
        _firedAlerts.Rows.ShouldBeEmpty();
    }

    /// <summary>A symbol that is not a ticker never reaches the database.</summary>
    [Fact]
    public async Task AMalformedTicker_ReadsNothing()
    {
        await BuildEvaluator().EvaluateAsync("BRK.B", TestContext.Current.CancellationToken);

        _settings.EnabledReadCount.ShouldBe(0);
    }

    /// <summary>A fall of 6% against both the window high and the oldest sample: signs agree, it fires.</summary>
    private static PriceWindow Falling() => new(
        Ticker: Symbol,
        Current: 141m,
        Oldest: 150m,
        Low: 141m,
        High: 150m,
        OldestAt: Now.AddMinutes(-60),
        NewestAt: Now,
        SampleCount: 60,
        LargestGap: TimeSpan.FromMinutes(1));

    /// <summary>A flat series: nothing to report at any threshold.</summary>
    private static PriceWindow Calm() => new(
        Ticker: Symbol,
        Current: 150m,
        Oldest: 150m,
        Low: 149.9m,
        High: 150.1m,
        OldestAt: Now.AddMinutes(-5),
        NewestAt: Now,
        SampleCount: 5,
        LargestGap: TimeSpan.FromMinutes(1));

    private Guid WatchedBy(string label, int windowMinutes = WindowMinutes)
    {
        // A deterministic id per label, so an ordering assertion is not at the mercy of Guid.NewGuid.
        var userId = new Guid(label.GetHashCode(StringComparison.Ordinal), 0, 0, new byte[8]);

        if (!AlertSetting
                .Create(
                    userId,
                    Symbol,
                    Threshold,
                    windowMinutes,
                    enabled: true,
                    AlertsOptions.DefaultMaxWindowMinutes)
                .TryPickT0(out var setting, out var invalid))
        {
            throw new InvalidOperationException(invalid.Message);
        }

        _settings.With(setting);

        return userId;
    }

    private AlertEvaluator BuildEvaluator(
        bool publisherThrows = false,
        bool cooldownRefusesEverything = false)
    {
        _firedAlerts = new FakeFiredAlertRepository(_journal);
        _publisher = new FakeAlertPublisher(_journal) { ThrowEveryTime = publisherThrows };
        _cooldowns = new FakeAlertCooldownStore(_clock) { RefuseEverything = cooldownRefusesEverything };

        return new AlertEvaluator(
            _settings,
            _windows,
            _cooldowns,
            new AlertDispatcher(_firedAlerts, _publisher, NullLogger<AlertDispatcher>.Instance),
            new AlertsOptions(
                AlertsOptions.DefaultMaxWindowMinutes,
                TimeSpan.FromMinutes(AlertsOptions.DefaultCooldownMinutes),
                AlertsOptions.DefaultHistoryLimit,
                AlertsOptions.DefaultMinimumSamples,
                MaxSampleGap),
            _clock,
            NullLogger<AlertEvaluator>.Instance);
    }
}
