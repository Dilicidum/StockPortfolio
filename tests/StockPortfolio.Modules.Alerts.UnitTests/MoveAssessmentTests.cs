using Shouldly;

using StockPortfolio.Modules.Alerts.Application.Evaluation;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.MarketData.Contracts;

namespace StockPortfolio.Tests;

/// <summary>The sign-agreement rule. These are red until Assess is written, and that is the intended state.</summary>
public sealed class MoveAssessmentTests
{
    private static readonly DateTimeOffset Origin = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Two decimal places, so a rule that rounds and a rule that does not both read as correct.</summary>
    private const decimal Tolerance = 0.005m;

    private const decimal Threshold = 5m;

    [Fact]
    public void OscillatingInsideTheBand_DoesNotFireEveryCycle()
    {
        // 150 -> 141 -> 149 -> 141 -> 149 ...  Threshold 5%.
        // Against the window low alone this fires a +5.67% RISE on every up-leg, forever.
        var fires = 0;

        foreach (var current in new[] { 149m, 141m, 149m, 141m, 149m, 141m })
        {
            var window = new PriceWindow(
                Ticker: "AAPL", Current: current, Oldest: 150m, Low: 141m, High: 150m,
                OldestAt: Origin, NewestAt: Origin.AddMinutes(60), SampleCount: 60,
                LargestGap: TimeSpan.FromMinutes(1));

            if (MoveAssessment.Assess(window, Threshold).Fires) { fires++; }
        }

        // Only the down-legs agree in sign. Without sign agreement this is 6.
        fires.ShouldBe(3);
    }

    [Fact]
    public void StraightFall_Fires_WithBothMeasurementsAgreeing()
    {
        // Opens 150, never higher, now 141. Endpoint and extreme are the same -6%.
        var verdict = MoveAssessment.Assess(Window(current: 141m, oldest: 150m, low: 141m, high: 150m), Threshold);

        verdict.Fires.ShouldBeTrue();
        verdict.Direction.ShouldBe(AlertDirection.Fall);
        verdict.ExtremePercent.ShouldBe(-6m, Tolerance);
        verdict.EndpointPercent.ShouldBe(-6m, Tolerance);
        verdict.ReferencePrice.ShouldBe(150m);
        verdict.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void SlideOffTheWindowHigh_Fires_ThoughTheEndpointMoveIsWellUnderTheThreshold()
    {
        // Opens 145, peaks 150, bottoms 141, now 142. This is the entire reason extremes are in the
        // design: an endpoint-only comparison sees -2.07% and sleeps through a real slide off the high.
        var verdict = MoveAssessment.Assess(Window(current: 142m, oldest: 145m, low: 141m, high: 150m), Threshold);

        verdict.Fires.ShouldBeTrue();
        verdict.Direction.ShouldBe(AlertDirection.Fall);
        verdict.ExtremePercent.ShouldBe(-5.33m, Tolerance);
        verdict.EndpointPercent.ShouldBe(-2.07m, Tolerance);
        verdict.ReferencePrice.ShouldBe(150m);
        verdict.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void StraightRise_Fires_WithBothMeasurementsAgreeing()
    {
        // Opens 140, never lower, now 149.
        var verdict = MoveAssessment.Assess(Window(current: 149m, oldest: 140m, low: 140m, high: 149m), Threshold);

        verdict.Fires.ShouldBeTrue();
        verdict.Direction.ShouldBe(AlertDirection.Rise);
        verdict.ExtremePercent.ShouldBe(6.43m, Tolerance);
        verdict.EndpointPercent.ShouldBe(6.43m, Tolerance);
        verdict.ReferencePrice.ShouldBe(140m);
        verdict.Reason.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ExtremeAndEndpointDisagreeing_DoesNotFire_HoweverLargeTheExtremeIs()
    {
        // Opens 150, craters to 120, back to 149. The rise off the low is +24.17%, nearly five times the
        // threshold, and the ticker is down on the window. This is the case the threshold cannot catch:
        // raise it and case two stops firing, lower it and this one starts. Only the signs separate them.
        var verdict = MoveAssessment.Assess(Window(current: 149m, oldest: 150m, low: 120m, high: 150m), Threshold);

        verdict.Fires.ShouldBeFalse();
    }

    [Fact]
    public void MoveUnderTheThreshold_DoesNotFire_EvenWithBothMeasurementsAgreeing()
    {
        // Opens 150, now 147.5: -1.67% both ways, agreeing and under 5%. Sign agreement is a second
        // condition, not a replacement for the threshold.
        var verdict = MoveAssessment.Assess(Window(current: 147.5m, oldest: 150m, low: 147.5m, high: 150m), Threshold);

        verdict.Fires.ShouldBeFalse();
    }

    private static PriceWindow Window(decimal current, decimal oldest, decimal low, decimal high) =>
        new(
            Ticker: "AAPL",
            Current: current,
            Oldest: oldest,
            Low: low,
            High: high,
            OldestAt: Origin,
            NewestAt: Origin.AddMinutes(60),
            SampleCount: 60,
            LargestGap: TimeSpan.FromMinutes(1));
}
