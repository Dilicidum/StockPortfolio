using System.Globalization;

using Shouldly;

using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Application.Dashboard;
using StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboard;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Tests;

public sealed class DashboardCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 5, TimeSpan.Zero);

    private static readonly Dictionary<string, string> NoNames = new(StringComparer.Ordinal);

    [Fact]
    public void Position_ProfitInCurrencyAndPercent()
    {
        var result = Calculate([Row("AAPL", 20m, 125m)], Prices(("AAPL", 150m)));

        var position = result.Positions.ShouldHaveSingleItem();

        position.Cost.ShouldBe(Money.Usd(2500m));
        position.MarketValue.ShouldBe(Money.Usd(3000m));
        position.Profit.ShouldBe(Money.Usd(500m));
        position.ProfitPercent.ShouldBe("20.00");
        position.IsLastKnown.ShouldBeFalse();
    }

    [Fact]
    public void Position_Loss_IsNegativeNotAbsolute()
    {
        var result = Calculate([Row("AAPL", 10m, 100m)], Prices(("AAPL", 80m)));

        var position = result.Positions.ShouldHaveSingleItem();

        position.Profit.ShouldBe(Money.Usd(-200m));
        position.ProfitPercent.ShouldBe("-20.00");
    }

    [Fact]
    public void Totals_SumAcrossPositions()
    {
        var result = Calculate(
            [Row("AAPL", 20m, 125m), Row("MSFT", 10m, 200m)],
            Prices(("AAPL", 150m), ("MSFT", 250m)));

        result.Totals.Value.ShouldBe(Money.Usd(5500m));
        result.Totals.Cost.ShouldBe(Money.Usd(4500m));
        result.Totals.Profit.ShouldBe(Money.Usd(1000m));
        result.Totals.PositionCount.ShouldBe(2);
        result.Totals.PricedPositionCount.ShouldBe(2);
    }

    [Fact]
    public void Totals_ExcludeNullPricePosition()
    {
        var result = Calculate(
            [Row("AAPL", 20m, 125m), Row("TSLA", 5m, 200m)],
            Prices(("AAPL", 150m)));

        result.Totals.Value.ShouldBe(Money.Usd(3000m));
        result.Totals.PositionCount.ShouldBe(2);

        // Flagged rather than counted as $0 — the footnote on the page is driven by this difference.
        result.Totals.PricedPositionCount.ShouldBe(1);

        var unpriced = result.Positions.Single(position => position.Ticker == "TSLA");

        unpriced.CurrentPrice.ShouldBeNull();
        unpriced.MarketValue.ShouldBeNull();
        unpriced.Profit.ShouldBeNull();
        unpriced.ProfitPercent.ShouldBeNull();
        unpriced.ObservedAt.ShouldBeNull();

        unpriced.Cost.ShouldBe(Money.Usd(1000m));
    }

    [Fact]
    public void Totals_CostExcludesUnpricedPositions()
    {
        var result = Calculate(
            [Row("AAPL", 20m, 125m), Row("TSLA", 5m, 200m)],
            Prices(("AAPL", 150m)));

        // Cost over the same subset as Value: $3500 here would report a $500 loss on a portfolio up $500.
        result.Totals.Cost.ShouldBe(Money.Usd(2500m));
        result.Totals.Profit.ShouldBe(Money.Usd(500m));
        result.Totals.ProfitPercent.ShouldBe("20.00");
    }

    [Fact]
    public void Totals_NothingPriced_ProfitPercentIsNullNotZero()
    {
        var result = Calculate([Row("AAPL", 20m, 125m), Row("TSLA", 5m, 200m)], Prices());

        result.Totals.PricedPositionCount.ShouldBe(0);

        result.Totals.ProfitPercent.ShouldBeNull(
            "\"0.00\" tells the holder their portfolio is exactly break-even at the moment nothing "
            + "about it is known — the argument DashboardPosition.Weight already makes one level down");
    }

    [Fact]
    public void Totals_NoPositions_ProfitPercentIsNull()
    {
        DashboardCalculator.Calculate([], Prices(), NoNames, Now).Totals.ProfitPercent.ShouldBeNull();
    }

    [Fact]
    public void Calculate_MixedCurrencies_Throws()
    {
        var rows = new[]
        {
            Row("AAPL", 1m, 100m),
            new HoldingRow(Guid.CreateVersion7(), "SAP", 1m, new Money(90m, "EUR")),
        };

        var thrown = Should.Throw<InvalidOperationException>(
            () => Calculate(rows, Prices(("AAPL", 150m), ("SAP", 95m))));

        // Silence is the failure mode this pins: totals labelled USD, summed across two currencies.
        thrown.Message.ShouldContain("EUR");
    }

    [Fact]
    public void Weight_SumsToOneHundredPercent()
    {
        var result = Calculate(
            [Row("AAPL", 3m, 100m), Row("MSFT", 7m, 50m), Row("NVDA", 11m, 10m)],
            Prices(("AAPL", 101.37m), ("MSFT", 33.33m), ("NVDA", 7.77m)));

        var weights = result.Positions
            .Select(position => decimal.Parse(position.Weight!, CultureInfo.InvariantCulture))
            .ToList();

        // Never an exact 100, and never fudge the largest row: three rows rounded to 0.01 can drift 0.015.
        weights.Sum().ShouldBe(100m, tolerance: result.Totals.PricedPositionCount * 0.005m);
    }

    [Fact]
    public void Weight_WithNullPricePosition_ExcludesFromDenominator()
    {
        var result = Calculate(
            [Row("AAPL", 20m, 125m), Row("TSLA", 5m, 200m)],
            Prices(("AAPL", 150m)));

        result.Positions.Single(position => position.Ticker == "AAPL").Weight.ShouldBe("100.00");

        // Null, not "0.00": zero is a claim about the share, and the share is unknown.
        result.Positions.Single(position => position.Ticker == "TSLA").Weight.ShouldBeNull();
    }

    [Fact]
    public void StalestObservedAt_IsMinOverPricedPositions()
    {
        var older = Now - TimeSpan.FromMinutes(30);

        var result = Calculate(
            [Row("AAPL", 20m, 125m), Row("MSFT", 10m, 200m), Row("TSLA", 5m, 200m)],
            new Dictionary<string, QuotedPrice>(StringComparer.Ordinal)
            {
                ["AAPL"] = new("AAPL", 150m, Now, IsLastKnown: false),
                ["MSFT"] = new("MSFT", 250m, older, IsLastKnown: true),
            });

        result.StalestObservedAt.ShouldBe(older);
        result.AsOf.ShouldBe(Now);

        Calculate([Row("AAPL", 20m, 125m)], Prices()).StalestObservedAt.ShouldBeNull();
    }

    private static GetDashboardResult Calculate(
        IReadOnlyList<HoldingRow> rows,
        IReadOnlyDictionary<string, QuotedPrice> prices,
        IReadOnlyDictionary<string, string>? names = null) =>
        DashboardCalculator.Calculate(rows, prices, names ?? NoNames, Now);

    private static HoldingRow Row(string ticker, decimal quantity, decimal averagePrice) =>
        new(Guid.CreateVersion7(), ticker, quantity, Money.Usd(averagePrice));

    private static Dictionary<string, QuotedPrice> Prices(params (string Ticker, decimal Price)[] quoted) =>
        quoted.ToDictionary(
            entry => entry.Ticker,
            entry => new QuotedPrice(entry.Ticker, entry.Price, Now, IsLastKnown: false),
            StringComparer.Ordinal);
}
