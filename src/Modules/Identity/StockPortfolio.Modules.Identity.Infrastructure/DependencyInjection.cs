using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Infrastructure;

/// <summary>Handler registrations, kept out of IdentityModule so the public seam stays one method.</summary>
internal static class DependencyInjection
{
    internal static IServiceCollection AddIdentityHandlers(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<RegisterUserCommand, RegisterUserResult>, RegisterUserCommandHandler>();
        services.AddScoped<ICommandHandler<LoginUserCommand, LoginUserResult>, LoginUserCommandHandler>();
        services.AddScoped<ICommandHandler<RefreshSessionCommand, RefreshSessionResult>, RefreshSessionCommandHandler>();
        services.AddScoped<ICommandHandler<RevokeSessionCommand, RevokeSessionResult>, RevokeSessionCommandHandler>();
        services.AddScoped<IQueryHandler<GetCurrentUserQuery, GetCurrentUserResult>, GetCurrentUserQueryHandler>();

        return services;
    }
}
