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
    public Task<OneOf<GetDashboardSettingsResult, InvalidInput>> Handle(
        SaveDashboardSettingsCommand command, CancellationToken ct) =>
        RefreshInterval.Create(command.RefreshIntervalSeconds).Match(
            interval => SaveAsync(command, interval, ct),
            invalid => Task.FromResult<OneOf<GetDashboardSettingsResult, InvalidInput>>(invalid));

    private async Task<OneOf<GetDashboardSettingsResult, InvalidInput>> SaveAsync(
        SaveDashboardSettingsCommand command, RefreshInterval interval, CancellationToken ct)
    {
        var settings = await repository.FindAsync(command.UserId, ct)
            ?? DashboardSettings.CreateDefault(command.UserId);
        settings.ChangeInterval(interval);
        await repository.SaveAsync(settings, ct);

        return new GetDashboardSettingsResult(interval.Seconds);
    }
}
