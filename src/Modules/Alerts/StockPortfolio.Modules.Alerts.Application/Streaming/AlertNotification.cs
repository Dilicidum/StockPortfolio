using System.Globalization;

using StockPortfolio.Modules.Alerts.Application.Evaluation;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Streaming;

/// <summary>What crosses the pub/sub channel and, unchanged, the server-sent-events frame.</summary>
public sealed record AlertNotification(
    Guid Id,
    Guid UserId,
    string Ticker,
    string Direction,
    string ChangePercent,
    string EndpointPercent,
    string TriggerPrice,
    string ReferencePrice,
    string Currency,
    DateTimeOffset FiredAt,
    bool IsSimulated,
    string Reason)
{
    /// <summary>The same two-decimal form the history route uses, so one row cannot render two ways.</summary>
    public const string PercentFormat = "0.00";

    /// <summary>Describes a saved row. The row is the source, so a pushed alert and a fetched one agree.</summary>
    public static AlertNotification From(FiredAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        // One currency for both prices: a trigger and the window extreme it was measured against are
        // the same instrument, so two currency fields could only ever disagree by being wrong.
        return new AlertNotification(
            alert.Id.Value,
            alert.UserId,
            alert.Ticker.Value,
            alert.Direction.ToString(),
            alert.ChangePercent.ToString(PercentFormat, CultureInfo.InvariantCulture),
            alert.EndpointPercent.ToString(PercentFormat, CultureInfo.InvariantCulture),
            alert.TriggerPrice.Amount.ToString(CultureInfo.InvariantCulture),
            alert.ReferencePrice.Amount.ToString(CultureInfo.InvariantCulture),
            alert.TriggerPrice.Currency,
            alert.FiredAt,
            alert.IsSimulated,
            MoveAssessment.Describe(alert.Direction, alert.ChangePercent));
    }
}
