using Shouldly;

using StockPortfolio.Modules.Alerts.Application.Evaluation;
using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.MarketData.Contracts;

namespace StockPortfolio.Tests;

public sealed class MoveAssessmentTests
{
    private static readonly DateTimeOffset Origin = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    // Two decimal places, so a rule that rounds and a rule that does not both read as correct.
    private const decimal Tolerance = 0.005m;

    private const decimal Threshold = 5m;

    [Fact]
    public void OscillatingInsideTheBand_DoesNotFireEveryCycle()
    {
        // Measured against the window low alone, this oscillation fires a +5.67% rise on every up-leg, for ever.
        var fires = 0;

        foreach (var current in new[] { 149m, 141m, 149m, 141m, 149m, 141m })
        {
            var window = new PriceWindow(
                Ticker: "AAPL", Current: current, Oldest: 150m, Low: 141m, High: 150m,
                OldestAt: Origin, NewestAt: Origin.AddMinutes(60), SampleCount: 60,
                LargestGap: TimeSpan.FromMinutes(1));

            if (MoveAssessment.Assess(window, Threshold).Fires) { fires++; }
        }

        // Only the down-legs agree in sign; without sign agreement this is 6.
        fires.ShouldBe(3);
    }

    [Fact]
    public void StraightFall_Fires_WithBothMeasurementsAgreeing()
    {
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
        // An endpoint-only comparison sees -2.07% here and sleeps through a real slide off the high.
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
        // No threshold separates this from the slide case above — raise it and that one stops firing; only the signs do.
        var verdict = MoveAssessment.Assess(Window(current: 149m, oldest: 150m, low: 120m, high: 150m), Threshold);

        verdict.Fires.ShouldBeFalse();
    }

    [Fact]
    public void MoveUnderTheThreshold_DoesNotFire_EvenWithBothMeasurementsAgreeing()
    {
        // Sign agreement is a second condition, not a replacement for the threshold.
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
