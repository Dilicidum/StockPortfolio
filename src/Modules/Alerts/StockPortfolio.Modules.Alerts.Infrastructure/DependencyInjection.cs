using Microsoft.Extensions.DependencyInjection;

using OneOf;

using StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;
using StockPortfolio.Modules.Alerts.Application.Settings.Queries.GetAlertSettings;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Alerts.Infrastructure;

/// <summary>Handler registrations, kept out of AlertsModule so the public seam stays one method.</summary>
internal static class DependencyInjection
{
    internal static IServiceCollection AddAlertsHandlers(this IServiceCollection services)
    {
        services.AddScoped<
            ICommandHandler<
                SaveAlertSettingCommand,
                OneOf<SaveAlertSettingResult, TickerNotHeld, WindowExceedsRetention, InvalidInput>>,
            SaveAlertSettingCommandHandler>();

        services.AddScoped<
            IQueryHandler<GetAlertSettingsQuery, IReadOnlyList<GetAlertSettingsResult>>,
            GetAlertSettingsQueryHandler>();

        return services;
    }
}
