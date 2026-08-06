using Microsoft.Extensions.DependencyInjection;

using OneOf;

using StockPortfolio.Modules.Identity.Application.Preferences.Commands.SaveAppearance;
using StockPortfolio.Modules.Identity.Application.Preferences.Queries.GetAppearance;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Infrastructure;

/// <summary>Handler registrations, kept out of IdentityModule so the public seam stays one method.</summary>
internal static class DependencyInjection
{
    internal static IServiceCollection AddIdentityHandlers(this IServiceCollection services)
    {
        // Only preferences remain. Register, login, refresh and logout are the framework's endpoints now,
        // and they talk to UserManager and SignInManager rather than to a handler.
        services.AddScoped<IQueryHandler<GetAppearanceQuery, GetAppearanceResult>, GetAppearanceQueryHandler>();
        services.AddScoped<
            ICommandHandler<SaveAppearanceCommand, OneOf<GetAppearanceResult, InvalidInput>>,
            SaveAppearanceCommandHandler>();

        return services;
    }
}
