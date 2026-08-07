using Shouldly;

using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Tests;

public sealed class FeedHealthRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private static FeedVerdict Verdict(DateTimeOffset? lastCycleAt, int targeted, int stored = 1) =>
        FeedHealthRule.Evaluate(lastCycleAt, targeted, stored, Interval, Now);

    [Fact]
    public void Verdict_NoCycleHasEverFinished_IsUnhealthy() =>
        Verdict(null, 0).ShouldBe(FeedVerdict.Unhealthy);

    // The case a rule built on "did we store any prices" gets wrong, and nothing in the app would ever say so.
    [Fact]
    public void Verdict_LastCycleHadNothingToPoll_IsHealthyNotBroken() =>
        Verdict(Now - TimeSpan.FromSeconds(30), 0).ShouldBe(FeedVerdict.Healthy);

    [Fact]
    public void Verdict_NothingToPollAndTheCycleIsOld_IsStillHealthy() =>
        Verdict(Now - TimeSpan.FromHours(6), 0).ShouldBe(FeedVerdict.Healthy);

    [Theory]
    [InlineData(1, FeedVerdict.Healthy)]
    [InlineData(179, FeedVerdict.Healthy)]
    [InlineData(180, FeedVerdict.Healthy)]
    [InlineData(181, FeedVerdict.Degraded)]
    [InlineData(599, FeedVerdict.Degraded)]
    [InlineData(600, FeedVerdict.Degraded)]
    [InlineData(601, FeedVerdict.Unhealthy)]
    public void Verdict_WithTargets_FallsThroughThreeThenTenIntervals(int ageSeconds, FeedVerdict expected) =>
        Verdict(Now - TimeSpan.FromSeconds(ageSeconds), 5).ShouldBe(expected);

    // The bands are multiples of the configured interval, so a 10-second setting must not read as 10 minutes of slack.
    [Fact]
    public void Verdict_BandsFollowTheConfiguredInterval_NotAFixedNumberOfMinutes()
    {
        var lastCycleAt = Now - TimeSpan.FromSeconds(200);

        FeedHealthRule.Evaluate(lastCycleAt, 5, 5, TimeSpan.FromSeconds(10), Now).ShouldBe(FeedVerdict.Unhealthy);
        FeedHealthRule.Evaluate(lastCycleAt, 5, 5, TimeSpan.FromSeconds(300), Now).ShouldBe(FeedVerdict.Healthy);
    }

    [Fact]
    public void Verdict_ACycleStampedInTheFuture_IsHealthyNotUnhealthy() =>
        Verdict(Now + TimeSpan.FromSeconds(30), 5).ShouldBe(FeedVerdict.Healthy);

    // Timing alone calls a dead provider healthy: the poller keeps finishing punctual cycles that fetch nothing.
    [Fact]
    public void Verdict_APunctualCycleThatStoredNothing_IsDegradedNotHealthy() =>
        Verdict(Now - TimeSpan.FromSeconds(30), 5, stored: 0).ShouldBe(FeedVerdict.Degraded);

    // Partial provider failure is not a dead feed; only storing nothing at all is.
    [Fact]
    public void Verdict_APunctualCycleThatStoredSome_IsHealthy() =>
        Verdict(Now - TimeSpan.FromSeconds(30), 5, stored: 1).ShouldBe(FeedVerdict.Healthy);
}
