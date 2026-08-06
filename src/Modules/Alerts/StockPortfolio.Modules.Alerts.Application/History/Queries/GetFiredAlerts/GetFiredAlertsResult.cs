using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Application.History.Queries.GetFiredAlerts;

/// <summary>One row of history on the wire. Percentages are strings; the server computed them.</summary>
public sealed record GetFiredAlertsResult(
    Guid Id,
    string Ticker,
    AlertDirection Direction,
    string ChangePercent,
    string EndpointPercent,
    Money TriggerPrice,
    Money ReferencePrice,
    DateTimeOffset FiredAt,
    bool IsSimulated,
    string Reason);
