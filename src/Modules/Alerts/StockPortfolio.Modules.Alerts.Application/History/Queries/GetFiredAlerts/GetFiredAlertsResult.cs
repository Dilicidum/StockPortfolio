using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Application.History.Queries.GetFiredAlerts;

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
