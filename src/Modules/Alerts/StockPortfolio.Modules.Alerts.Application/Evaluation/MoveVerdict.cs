using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Evaluation;

/// <summary>One window judged against one threshold: whether it fires, by how much, and against what.</summary>
public sealed record MoveVerdict(
    bool Fires,
    AlertDirection Direction,
    decimal ExtremePercent,
    decimal EndpointPercent,
    decimal ReferencePrice,
    string Reason);
