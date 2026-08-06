using Microsoft.Extensions.DependencyInjection;

using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Alerts.Application.History.Queries.GetFiredAlerts;
using StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;
using StockPortfolio.Modules.Alerts.Application.Settings.Queries.GetAlertSettings;
using StockPortfolio.Modules.Alerts.Application.Simulation.Commands.SimulateAlert;
using StockPortfolio.Modules.Alerts.Application.Streaming.Commands.IssueStreamTicket;
using StockPortfolio.Modules.Alerts.Application.Streaming.Commands.RedeemStreamTicket;
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

        services.AddScoped<
            IQueryHandler<GetFiredAlertsQuery, IReadOnlyList<GetFiredAlertsResult>>,
            GetFiredAlertsQueryHandler>();

        services.AddScoped<
            ICommandHandler<IssueStreamTicketCommand, IssueStreamTicketResult>,
            IssueStreamTicketCommandHandler>();

        services.AddScoped<
            ICommandHandler<RedeemStreamTicketCommand, OneOf<Guid, TicketNotRecognised>>,
            RedeemStreamTicketCommandHandler>();

        services.AddScoped<
            ICommandHandler<SimulateAlertCommand, OneOf<Success, NoPositionToSimulate>>,
            SimulateAlertCommandHandler>();

        return services;
    }
}
