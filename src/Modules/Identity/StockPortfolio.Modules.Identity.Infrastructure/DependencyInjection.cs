using Microsoft.Extensions.DependencyInjection;

using OneOf;

using StockPortfolio.Modules.Identity.Application.Preferences.Commands.SaveAppearance;
using StockPortfolio.Modules.Identity.Application.Preferences.Queries.GetAppearance;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Infrastructure;

internal static class DependencyInjection
{
    internal static IServiceCollection AddIdentityHandlers(this IServiceCollection services)
    {
        services.AddScoped<IQueryHandler<GetAppearanceQuery, GetAppearanceResult>, GetAppearanceQueryHandler>();
        services.AddScoped<
            ICommandHandler<SaveAppearanceCommand, OneOf<GetAppearanceResult, InvalidInput>>,
            SaveAppearanceCommandHandler>();

        return services;
    }
}
