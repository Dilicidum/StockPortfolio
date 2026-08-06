using System.Globalization;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.Evaluation;
using StockPortfolio.Modules.Alerts.Application.Streaming;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Alerts.Application.History.Queries.GetFiredAlerts;

/// <summary>Reads recent alerts. There is no failure case: an empty history is not an error.</summary>
public sealed class GetFiredAlertsQueryHandler(IFiredAlertRepository alerts, AlertsOptions options)
    : IQueryHandler<GetFiredAlertsQuery, IReadOnlyList<GetFiredAlertsResult>>
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<GetFiredAlertsResult>> Handle(
        GetFiredAlertsQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Clamped, not rejected: ?limit=0 and ?limit=5000 are both somebody asking for a list, and a
        // 400 on a query string that names a sensible thing badly helps nobody.
        var limit = Math.Clamp(query.Limit, 1, options.HistoryLimit);

        var rows = await alerts.ListRecentAsync(query.UserId, limit, ct);

        return [.. rows.Select(Describe)];
    }

    /// <summary>The same sentence and the same two decimals the pushed frame carries, from the same code.</summary>
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
