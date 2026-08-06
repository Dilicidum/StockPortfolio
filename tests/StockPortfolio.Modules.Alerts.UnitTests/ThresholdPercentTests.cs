using Shouldly;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Tests;

public sealed class ThresholdPercentTests
{
    [Theory]
    [InlineData(0.01)]
    [InlineData(5)]
    [InlineData(99.99)]
    [InlineData(100)]
    public void Create_AcceptsAnythingAboveZeroUpToAHundred(decimal raw) =>
        ThresholdPercent.Create(raw).AsT0.Value.ShouldBe(raw);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(100.01)]
    [InlineData(1000)]
    public void Create_RejectsZeroNegativeAndOverAHundred(decimal raw) =>
        ThresholdPercent.Create(raw).AsT1.Field.ShouldBe("thresholdPercent");

    /// <summary>numeric(5,2) is the column, so the entity rounds before it judges — never after.</summary>
    [Fact]
    public void Create_RoundsToTheStoredScale_BeforeItValidates()
    {
        ThresholdPercent.Create(5.126m).AsT0.Value.ShouldBe(5.13m);

        // Rounds to 0.00, which the column would store as zero — so it is rejected, not silently kept.
        ThresholdPercent.Create(0.001m).AsT1.Field.ShouldBe("thresholdPercent");
    }

    [Fact]
    public void Equality_IsOnTheRoundedValue() =>
        ThresholdPercent.Create(5.001m).AsT0.ShouldBe(ThresholdPercent.Create(5m).AsT0);
}
