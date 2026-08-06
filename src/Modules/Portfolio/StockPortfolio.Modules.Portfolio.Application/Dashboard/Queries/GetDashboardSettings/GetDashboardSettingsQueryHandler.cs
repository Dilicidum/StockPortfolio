using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboardSettings;

public sealed class GetDashboardSettingsQueryHandler(IDashboardSettingsRepository repository)
    : IQueryHandler<GetDashboardSettingsQuery, GetDashboardSettingsResult>
{
    public async Task<GetDashboardSettingsResult> Handle(GetDashboardSettingsQuery query, CancellationToken ct)
    {
        var settings = await repository.FindAsync(query.UserId, ct) ?? DashboardSettings.CreateDefault(query.UserId);

        return new GetDashboardSettingsResult(settings.RefreshInterval.Seconds);
    }
}
