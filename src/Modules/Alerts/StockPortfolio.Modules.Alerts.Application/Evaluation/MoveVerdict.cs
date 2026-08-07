using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Evaluation;

public sealed record MoveVerdict(
    bool Fires,
    AlertDirection Direction,
    decimal ExtremePercent,
    decimal EndpointPercent,
    decimal ReferencePrice,
    string Reason);
