using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Alerts.Application.Settings.Queries.GetAlertSettings;

public sealed class GetAlertSettingsQueryHandler(IAlertSettingRepository settings)
    : IQueryHandler<GetAlertSettingsQuery, IReadOnlyList<GetAlertSettingsResult>>
{
    public async Task<IReadOnlyList<GetAlertSettingsResult>> Handle(
        GetAlertSettingsQuery query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var stored = await settings.ListForUserAsync(query.UserId, ct);

        return
        [
            .. stored.Select(setting => new GetAlertSettingsResult(
                setting.Ticker.Value,
                setting.Threshold.Value,
                setting.Window.Minutes,
                setting.Enabled)),
        ];
    }
}
