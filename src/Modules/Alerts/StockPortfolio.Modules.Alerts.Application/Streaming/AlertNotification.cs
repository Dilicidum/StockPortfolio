using System.Globalization;

using StockPortfolio.Modules.Alerts.Application.Evaluation;
using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Application.Streaming;

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
    public const string PercentFormat = "0.00";

    public static AlertNotification From(FiredAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

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
