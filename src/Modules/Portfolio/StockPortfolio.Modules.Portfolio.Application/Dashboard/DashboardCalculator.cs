using System.Globalization;

using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboard;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Application.Dashboard;

/// <summary>The P&amp;L arithmetic, pure: rows in, prices in, a clock reading in, the result out.</summary>
public static class DashboardCalculator
{
    /// <summary>Percent crosses the wire as a string; a bare decimal would be a JSON number.</summary>
    private const string PercentFormat = "0.00";

    /// <summary>Joins positions to prices and names, and computes every figure the dashboard shows.</summary>
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

        // One portfolio, one currency: every figure below is stamped with this code, including the
        // QuotedPrice values, which carry no currency of their own. Money.EnsureSameCurrency would
        // refuse a mixed set, but the sums here are bare decimals and so never ask it.
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

            // Cost is summed over the SAME subset as Value. Including an unpriced position's cost here
            // would report a loss the size of that position on a portfolio that is up.
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
                // Null, never "0.00", when no position could be priced: the same argument Weight makes
                // one level down. Zero here would report an exactly break-even portfolio.
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
            // Weight is null rather than 0: zero claims "this is 0% of your portfolio", and the truth
            // is that nobody knows. The unpriced position is out of the denominator entirely.
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

            // When this app fetched it, never Finnhub's last-trade time — that freezes at Friday's close.
            quote.ObservedAt,
            quote.IsLastKnown);
    }

    /// <summary>A share of a total as a display string, or null when the total says nothing.</summary>
    private static string? Percent(decimal part, decimal whole) =>
        whole == 0m ? null : Format(part / whole * 100m);

    private static string Format(decimal value) =>
        value.ToString(PercentFormat, CultureInfo.InvariantCulture);

    /// <summary>One row carried between the two passes; weight needs the denominator the first pass builds.</summary>
    private readonly record struct Line(HoldingRow Row, decimal Cost, QuotedPrice? Quote, decimal Value);
}
