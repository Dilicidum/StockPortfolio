using Microsoft.Extensions.DependencyInjection;

using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Portfolio.Application;
using StockPortfolio.Modules.Portfolio.Application.Dashboard.Commands.SaveDashboardSettings;
using StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboard;
using StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboardSettings;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.RemoveHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.SetHoldingVisibility;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.UpdateHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Queries.GetHoldings;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Infrastructure;

internal static class DependencyInjection
{
    internal static IServiceCollection AddPortfolioHandlers(this IServiceCollection services)
    {
        services.AddScoped<
            ICommandHandler<AddHoldingCommand, OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>>,
            AddHoldingCommandHandler>();

        services.AddScoped<
            ICommandHandler<UpdateHoldingCommand, OneOf<HoldingSummary, NotFound, InvalidInput>>,
            UpdateHoldingCommandHandler>();

        services.AddScoped<
            ICommandHandler<RemoveHoldingCommand, OneOf<Success, NotFound>>,
            RemoveHoldingCommandHandler>();

        services.AddScoped<
            ICommandHandler<SetHoldingVisibilityCommand, OneOf<Success, NotFound>>,
            SetHoldingVisibilityCommandHandler>();

        services.AddScoped<
            IQueryHandler<GetHoldingsQuery, IReadOnlyList<HoldingSummary>>,
            GetHoldingsQueryHandler>();

        services.AddScoped<
            IQueryHandler<GetDashboardQuery, GetDashboardResult>,
            GetDashboardQueryHandler>();

        services.AddScoped<
            IQueryHandler<GetDashboardSettingsQuery, GetDashboardSettingsResult>,
            GetDashboardSettingsQueryHandler>();

        services.AddScoped<
            ICommandHandler<SaveDashboardSettingsCommand, OneOf<GetDashboardSettingsResult, InvalidInput>>,
            SaveDashboardSettingsCommandHandler>();

        return services;
    }
}
