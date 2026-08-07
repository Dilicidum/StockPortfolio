using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Alerts.Application.Abstractions;

public sealed record FiredAlertRow(
    Guid Id,
    string Ticker,
    AlertDirection Direction,
    decimal ChangePercent,
    decimal EndpointPercent,
    Money TriggerPrice,
    Money ReferencePrice,
    DateTimeOffset FiredAt,
    bool IsSimulated);
