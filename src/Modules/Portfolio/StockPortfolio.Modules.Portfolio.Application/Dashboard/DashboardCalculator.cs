using System.Globalization;

using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboard;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Application.Dashboard;

public static class DashboardCalculator
{
    private const string PercentFormat = "0.00";

    public static GetDashboardResult Calculate(
        IReadOnlyList<HoldingRow> rows,
        IReadOnlyDictionary<string, QuotedPrice> prices,
        IReadOnlyDictionary<string, string> names,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(names);

        var currency = rows.Count > 0 ? rows[0].AveragePrice.Currency : Money.UsdCurrencyCode;

        var foreign = rows.FirstOrDefault(
            row => !string.Equals(row.AveragePrice.Currency, currency, StringComparison.Ordinal));

        if (foreign is not null)
        {
            throw new InvalidOperationException(
                $"Cannot total a portfolio holding {currency} alongside {foreign.Ticker} in "
                + $"{foreign.AveragePrice.Currency}: every total is labelled with the first row's "
                + "currency, so a mixed set would be silently mislabelled rather than converted.");
        }

        var lines = new List<Line>(rows.Count);
        var totalValue = 0m;
        var totalCost = 0m;
        var pricedCount = 0;
        DateTimeOffset? stalest = null;

        foreach (var row in rows)
        {
            var cost = row.AveragePrice.Amount * row.Quantity;

            if (!prices.TryGetValue(row.Ticker, out var quote))
            {
                lines.Add(new Line(row, cost, null, 0m));
                continue;
            }

            var value = quote.Price * row.Quantity;

            totalValue += value;

            // Summed over the same subset as Value: an unpriced position's cost here reports a false loss.
            totalCost += cost;
            pricedCount++;

            if (stalest is null || quote.ObservedAt < stalest)
            {
                stalest = quote.ObservedAt;
            }

            lines.Add(new Line(row, cost, quote, value));
        }

        var positions = new List<DashboardPosition>(lines.Count);

        foreach (var line in lines)
        {
            positions.Add(ToPosition(
                line,
                currency,
                totalValue,
                names.TryGetValue(line.Row.Ticker, out var name) ? name : null));
        }

        return new GetDashboardResult(
            positions,
            new DashboardTotals(
                new Money(totalValue, currency),
                new Money(totalCost, currency),
                new Money(totalValue - totalCost, currency),
                Percent(totalValue - totalCost, totalCost),
                rows.Count,
                pricedCount),
            asOf,
            stalest);
    }

    private static DashboardPosition ToPosition(
        Line line,
        string currency,
        decimal totalValue,
        string? name)
    {
        var cost = new Money(line.Cost, currency);

        if (line.Quote is not { } quote)
        {
            return new DashboardPosition(
                line.Row.Id,
                line.Row.Ticker,
                line.Row.Quantity,
                line.Row.AveragePrice,
                cost,
                name,
                CurrentPrice: null,
                MarketValue: null,
                Profit: null,
                ProfitPercent: null,
                Weight: null,
                ObservedAt: null,
                IsLastKnown: false);
        }

        return new DashboardPosition(
            line.Row.Id,
            line.Row.Ticker,
            line.Row.Quantity,
            line.Row.AveragePrice,
            cost,
            name,
            new Money(quote.Price, currency),
            new Money(line.Value, currency),
            new Money(line.Value - line.Cost, currency),
            Percent(line.Value - line.Cost, line.Cost),
            Percent(line.Value, totalValue),
            quote.ObservedAt,
            quote.IsLastKnown);
    }

    private static string? Percent(decimal part, decimal whole) =>
        whole == 0m ? null : Format(part / whole * 100m);

    private static string Format(decimal value) =>
        value.ToString(PercentFormat, CultureInfo.InvariantCulture);

    private readonly record struct Line(HoldingRow Row, decimal Cost, QuotedPrice? Quote, decimal Value);
}
