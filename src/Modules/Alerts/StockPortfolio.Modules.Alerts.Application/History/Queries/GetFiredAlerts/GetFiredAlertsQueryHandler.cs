using System.Globalization;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Evaluation;
using StockPortfolio.Modules.Alerts.Application.Streaming;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Alerts.Application.History.Queries.GetFiredAlerts;

public sealed class GetFiredAlertsQueryHandler(IFiredAlertRepository alerts, AlertsOptions options)
    : IQueryHandler<GetFiredAlertsQuery, IReadOnlyList<GetFiredAlertsResult>>
{
    public async Task<IReadOnlyList<GetFiredAlertsResult>> Handle(
        GetFiredAlertsQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = Math.Clamp(query.Limit, 1, options.HistoryLimit);

        var rows = await alerts.ListRecentAsync(query.UserId, limit, ct);

        return [.. rows.Select(Describe)];
    }

    private static GetFiredAlertsResult Describe(FiredAlertRow row) => new(
        row.Id,
        row.Ticker,
        row.Direction,
        row.ChangePercent.ToString(AlertNotification.PercentFormat, CultureInfo.InvariantCulture),
        row.EndpointPercent.ToString(AlertNotification.PercentFormat, CultureInfo.InvariantCulture),
        row.TriggerPrice,
        row.ReferencePrice,
        row.FiredAt,
        row.IsSimulated,
        MoveAssessment.Describe(row.Direction, row.ChangePercent));
}
