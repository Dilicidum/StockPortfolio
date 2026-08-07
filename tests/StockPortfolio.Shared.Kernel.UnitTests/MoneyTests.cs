using Shouldly;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void Usd_SetsCurrencyToUsd()
    {
        var money = Money.Usd(12.34m);

        money.Currency.ShouldBe("USD");
        money.Amount.ShouldBe(12.34m);
    }

    [Fact]
    public void Zero_SetsAmountToZeroInGivenCurrency()
    {
        var money = Money.Zero("EUR");

        money.Amount.ShouldBe(0m);
        money.Currency.ShouldBe("EUR");
    }

    [Fact]
    public void Add_SameCurrency_Sums()
    {
        var sum = Money.Usd(10.50m).Add(Money.Usd(4.25m));

        sum.Amount.ShouldBe(14.75m);
        sum.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Add_DifferentCurrency_Throws() =>
        Should.Throw<InvalidOperationException>(() => Money.Usd(10m).Add(new Money(10m, "EUR")));

    [Fact]
    public void Add_DifferentlyCasedSameCurrency_Sums()
    {
        var sum = new Money(1m, "usd").Add(Money.Usd(2m));

        sum.Amount.ShouldBe(3m);
    }

    [Fact]
    public void Subtract_SameCurrency_Subtracts()
    {
        var difference = Money.Usd(10m).Subtract(Money.Usd(12.5m));

        difference.Amount.ShouldBe(-2.5m);
        difference.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Subtract_DifferentCurrency_Throws() =>
        Should.Throw<InvalidOperationException>(() => Money.Usd(10m).Subtract(new Money(1m, "GBP")));

    [Fact]
    public void Multiply_ByQuantity_ScalesAmountAndKeepsCurrency()
    {
        var total = Money.Usd(19.99m).Multiply(3m);

        total.Amount.ShouldBe(59.97m);
        total.Currency.ShouldBe("USD");
    }

    [Fact]
    public void AdditionOperator_MatchesAdd() =>
        (Money.Usd(1.10m) + Money.Usd(2.20m)).ShouldBe(Money.Usd(3.30m));

    [Fact]
    public void SubtractionOperator_MatchesSubtract() =>
        (Money.Usd(5m) - Money.Usd(1.25m)).ShouldBe(Money.Usd(3.75m));

    [Fact]
    public void MultiplicationOperator_MatchesMultiply() =>
        (Money.Usd(2.5m) * 4m).ShouldBe(Money.Usd(10m));

    [Fact]
    public void Add_IsCommutative_AcrossCurrencyCasing()
    {
        // Record equality is ordinal, so without the constructor normalising currency these two sums compare unequal.
        var lowerFirst = new Money(1m, "usd").Add(Money.Usd(2m));
        var upperFirst = Money.Usd(1m).Add(new Money(2m, "usd"));

        lowerFirst.ShouldBe(upperFirst);
        lowerFirst.Currency.ShouldBe("USD");
        upperFirst.Currency.ShouldBe("USD");
    }

    [Fact]
    public void Equality_SameAmountAndCurrency_AreEqual() =>
        Money.Usd(7m).ShouldBe(new Money(7m, "USD"));

    [Fact]
    public void Equality_SameAmountDifferentCurrency_AreNotEqual() =>
        Money.Usd(7m).ShouldNotBe(new Money(7m, "EUR"));
}
