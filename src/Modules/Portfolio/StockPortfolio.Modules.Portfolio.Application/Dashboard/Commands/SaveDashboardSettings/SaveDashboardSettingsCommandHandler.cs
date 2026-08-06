using OneOf;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboardSettings;
using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Dashboard.Commands.SaveDashboardSettings;

public sealed class SaveDashboardSettingsCommandHandler(IDashboardSettingsRepository repository)
    : ICommandHandler<SaveDashboardSettingsCommand, OneOf<GetDashboardSettingsResult, InvalidInput>>
{
    public async Task<OneOf<GetDashboardSettingsResult, InvalidInput>> Handle(
        SaveDashboardSettingsCommand command, CancellationToken ct)
    {
        if (!RefreshInterval.Create(command.RefreshIntervalSeconds).TryPickT0(out var interval, out var invalid))
        {
            return invalid;
        }

        var settings = await repository.FindAsync(command.UserId, ct)
            ?? DashboardSettings.CreateDefault(command.UserId);
        settings.ChangeInterval(interval);
        await repository.SaveAsync(settings, ct);

        return new GetDashboardSettingsResult(interval.Seconds);
    }
}
