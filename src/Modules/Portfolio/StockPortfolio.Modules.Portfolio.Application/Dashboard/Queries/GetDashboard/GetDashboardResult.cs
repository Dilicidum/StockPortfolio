using System.Text.Json.Serialization;

using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboard;

public sealed record DashboardPosition(
    Guid Id,
    string Ticker,
    decimal Quantity,
    Money AveragePrice,
    Money Cost,

    // JsonIgnoreCondition.Never on every nullable member: the host's WhenWritingNull would drop it entirely.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Money? CurrentPrice,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Money? MarketValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] Money? Profit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ProfitPercent,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Weight,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? ObservedAt,
    bool IsLastKnown);

public sealed record DashboardTotals(
    Money Value,
    Money Cost,
    Money Profit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? ProfitPercent,
    int PositionCount,
    int PricedPositionCount);

public sealed record GetDashboardResult(
    IReadOnlyList<DashboardPosition> Positions,
    DashboardTotals Totals,
    DateTimeOffset AsOf,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTimeOffset? StalestObservedAt);
