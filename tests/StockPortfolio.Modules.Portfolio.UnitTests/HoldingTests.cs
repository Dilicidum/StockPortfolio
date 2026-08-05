using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Tests;

public sealed class HoldingTests
{
    private static readonly Guid User = Guid.CreateVersion7();
    private static readonly Ticker Aapl = Ticker.Create("AAPL").AsT0;

    private static readonly FakeTimeProvider Clock =
        new(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));

    private static Holding At(decimal quantity, decimal price) =>
        Holding.Create(User, Aapl, quantity, Money.Usd(price), Clock).AsT0;

    // The canonical case from Initial.md:104.
    [Fact]
    public void Merge_TenAtHundredPlusTenAtOneFifty_GivesTwentyAtOneTwentyFive()
    {
        var holding = At(10m, 100m);

        holding.Merge(10m, Money.Usd(150m), Clock).IsT0.ShouldBeTrue();

        holding.Quantity.ShouldBe(20m);
        holding.AveragePrice.ShouldBe(Money.Usd(125m));
    }

    [Fact]
    public void Merge_ThreeSuccessivePurchases_WeightsCorrectly()
    {
        var holding = At(10m, 100m);

        holding.Merge(5m, Money.Usd(200m), Clock);
        holding.Merge(5m, Money.Usd(50m), Clock);

        holding.Quantity.ShouldBe(20m);
        holding.AveragePrice.ShouldBe(Money.Usd(112.50m));
    }

    // The scale half of the rounding decision, on the example README quotes: 1 @ 0.333333 merged with
    // 2 @ 0.666667 weights to 0.5555556666..., which no column can hold.
    [Fact]
    public void Merge_WeightedAverageRunsPastSixDecimals_IsStoredAtSix()
    {
        var holding = At(1m, 0.333333m);

        holding.Merge(2m, Money.Usd(0.666667m), Clock);

        holding.AveragePrice.Amount.ShouldBe(0.555556m);
    }

    // The MODE half, and the pair is what pins it. (0.123456 + 0.123457) / 2 is 0.1234565 exactly, so
    // ToEven and ToZero keep the even 6 while AwayFromZero and ToPositiveInfinity go up.
    [Fact]
    public void Merge_WeightedAverageIsAMidpointAfterAnEvenDigit_RoundsDown()
    {
        var holding = At(1m, 0.123456m);

        holding.Merge(1m, Money.Usd(0.123457m), Clock);

        holding.AveragePrice.Amount.ShouldBe(
            0.123456m,
            "a midpoint after an even digit stays put under MidpointRounding.ToEven; AwayFromZero "
            + "would give 0.123457.");
    }

    // The other half: (0.123457 + 0.123458) / 2 is 0.1234575, a midpoint after an ODD digit, so ToEven
    // rounds UP. Without this case, MidpointRounding.ToZero would also pass the test above.
    [Fact]
    public void Merge_WeightedAverageIsAMidpointAfterAnOddDigit_RoundsUp()
    {
        var holding = At(1m, 0.123457m);

        holding.Merge(1m, Money.Usd(0.123458m), Clock);

        holding.AveragePrice.Amount.ShouldBe(
            0.123458m,
            "a midpoint after an odd digit goes up under MidpointRounding.ToEven; ToZero and "
            + "ToNegativeInfinity would both give 0.123457.");
    }

    // Rounding is applied on every write path, not only on Merge's division. Without it the 201 body
    // shows what the caller typed while the column stores something else, and the number the user was
    // just shown changes by itself on the next read.
    [Fact]
    public void Create_PriceRunsPastSixDecimals_IsStoredAtSix() =>
        At(10m, 100.1234567m).AveragePrice.Amount.ShouldBe(100.123457m);

    [Fact]
    public void Create_QuantityRunsPastSixDecimals_IsStoredAtSix() =>
        At(10.1234567m, 100m).Quantity.ShouldBe(10.123457m);

    [Fact]
    public void Correct_PriceRunsPastSixDecimals_IsStoredAtSix()
    {
        var holding = At(10m, 100m);

        holding.Correct(10m, Money.Usd(100.1234567m), Clock).IsT0.ShouldBeTrue();

        holding.AveragePrice.Amount.ShouldBe(100.123457m);
    }

    [Fact]
    public void Correct_QuantityRunsPastSixDecimals_IsStoredAtSix()
    {
        var holding = At(10m, 100m);

        holding.Correct(10.1234567m, Money.Usd(100m), Clock).IsT0.ShouldBeTrue();

        holding.Quantity.ShouldBe(10.123457m);
    }

    [Fact]
    public void Merge_QuantityRunsPastSixDecimals_IsStoredAtSix()
    {
        var holding = At(10m, 100m);

        holding.Merge(0.1234567m, Money.Usd(100m), Clock).IsT0.ShouldBeTrue();

        holding.Quantity.ShouldBe(10.123457m);
    }

    // numeric(18,6) holds twelve integer digits. Above that the INSERT raises 22003 and the caller
    // gets a bare 500 for input the two validation layers both waved through.
    [Fact]
    public void Create_QuantityAboveWhatTheColumnHolds_ReturnsInvalidInput() =>
        Holding.Create(User, Aapl, 1_000_000_000_000m, Money.Usd(100m), Clock).AsT1.Field.ShouldBe("quantity");

    [Fact]
    public void Create_PriceAboveWhatTheColumnHolds_ReturnsInvalidInput() =>
        Holding.Create(User, Aapl, 10m, Money.Usd(1_000_000_000_000m), Clock).AsT1.Field.ShouldBe("price");

    [Fact]
    public void Create_QuantityExactlyAtTheColumnCeiling_IsAccepted() =>
        Holding.Create(User, Aapl, 999_999_999_999.999999m, Money.Usd(100m), Clock).IsT0.ShouldBeTrue();

    [Fact]
    public void Correct_QuantityAboveWhatTheColumnHolds_ReturnsInvalidInput() =>
        At(10m, 100m).Correct(1_000_000_000_000m, Money.Usd(100m), Clock).AsT1.Field.ShouldBe("quantity");

    // The one the per-value check cannot catch: each increment clears the ceiling, their sum does not.
    [Fact]
    public void Merge_SumOfTwoLegalQuantitiesAboveTheCeiling_ReturnsInvalidInput()
    {
        var holding = At(600_000_000_000m, 100m);

        holding.Merge(600_000_000_000m, Money.Usd(100m), Clock).AsT1.Field.ShouldBe("quantity");

        holding.Quantity.ShouldBe(600_000_000_000m, "a rejected merge must leave the position alone");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0000001)]
    public void Merge_QuantityBelowOneMicroUnit_ReturnsInvalidInput(decimal quantity) =>
        At(10m, 100m).Merge(quantity, Money.Usd(150m), Clock).AsT1.Field.ShouldBe("quantity");

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Merge_NonPositivePrice_ReturnsInvalidInput(decimal price) =>
        At(10m, 100m).Merge(5m, Money.Usd(price), Clock).AsT1.Field.ShouldBe("price");

    // Create is the only door into the aggregate, and it validates through the same rules Merge does.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.0000001)]
    public void Create_QuantityBelowOneMicroUnit_ReturnsInvalidInput(decimal quantity) =>
        Holding.Create(User, Aapl, quantity, Money.Usd(100m), Clock).AsT1.Field.ShouldBe("quantity");

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_NonPositivePrice_ReturnsInvalidInput(decimal price) =>
        Holding.Create(User, Aapl, 10m, Money.Usd(price), Clock).AsT1.Field.ShouldBe("price");

    // Money.Add THROWS on a currency mismatch, so Merge must compare before it does any arithmetic.
    [Fact]
    public void Merge_DifferentCurrency_ReturnsInvalidInput_RatherThanThrowing() =>
        At(10m, 100m).Merge(5m, new Money(150m, "EUR"), Clock).AsT1.Field.ShouldBe("price");

    [Fact]
    public void Correct_DifferentCurrency_ReturnsInvalidInput_RatherThanThrowing() =>
        At(10m, 100m).Correct(5m, new Money(150m, "EUR"), Clock).AsT1.Field.ShouldBe("price");

    [Fact]
    public void Merge_LeavesQuantityUntouched_WhenItRejects()
    {
        var holding = At(10m, 100m);

        holding.Merge(0m, Money.Usd(150m), Clock);

        holding.Quantity.ShouldBe(10m);
        holding.AveragePrice.ShouldBe(Money.Usd(100m));
    }

    [Fact]
    public void Correct_ReplacesRatherThanAverages()
    {
        var holding = At(10m, 100m);
        holding.Merge(10m, Money.Usd(150m), Clock);      // now 20 @ $125

        holding.Correct(10m, Money.Usd(100m), Clock).IsT0.ShouldBeTrue();

        holding.Quantity.ShouldBe(10m);
        holding.AveragePrice.ShouldBe(Money.Usd(100m));
    }

    [Fact]
    public void Create_NewHolding_IsVisible() => At(10m, 100m).IsVisible.ShouldBeTrue();

    [Fact]
    public void Merge_StampsUpdatedAt_LeavingCreatedAtAlone()
    {
        var holding = At(10m, 100m);
        var created = holding.CreatedAt;

        Clock.Advance(TimeSpan.FromMinutes(5));
        holding.Merge(10m, Money.Usd(150m), Clock);

        holding.CreatedAt.ShouldBe(created);
        holding.UpdatedAt.ShouldBe(created.AddMinutes(5));
    }
}
