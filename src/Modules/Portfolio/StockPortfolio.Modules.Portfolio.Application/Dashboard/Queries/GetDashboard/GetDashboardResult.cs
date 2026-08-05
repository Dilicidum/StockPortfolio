using System.Text.Json.Serialization;

using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboard;

/// <summary>One position, priced or not. Every nullable member means "unknown", which is never zero.</summary>
public sealed record DashboardPosition(
    Guid Id,
    string Ticker,
    decimal Quantity,
    Money AveragePrice,
    Money Cost,

    // JsonIgnoreCondition.Never on every nullable member, because Program.cs sets DefaultIgnoreCondition
    // to WhenWritingNull — which drops nullable value types too, so without these the field is ABSENT.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Money? CurrentPrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Money? MarketValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Money? Profit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ProfitPercent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Weight,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? ObservedAt,
    bool IsLastKnown);

/// <summary>The KPI row. Value and Cost are summed over the priced positions and over exactly the same ones.</summary>
public sealed record DashboardTotals(
    Money Value,
    Money Cost,
    Money Profit,

    // Null when nothing could be priced, for the same reason a row's Weight is: "0.00" claims the
    // portfolio is exactly break-even at the moment nothing about it is known.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ProfitPercent,
    int PositionCount,
    int PricedPositionCount);

/// <summary>The dashboard. An empty portfolio is a valid one, so this result has no failure sibling.</summary>
public sealed record GetDashboardResult(
    IReadOnlyList<DashboardPosition> Positions,
    DashboardTotals Totals,
    DateTimeOffset AsOf,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? StalestObservedAt);
