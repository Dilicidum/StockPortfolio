using Microsoft.Extensions.DependencyInjection;

using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Identity.Application;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;
using StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Infrastructure;

/// <summary>Handler registrations, kept out of IdentityModule so the public seam stays one method.</summary>
internal static class DependencyInjection
{
    internal static IServiceCollection AddIdentityHandlers(this IServiceCollection services)
    {
        services.AddScoped<
            ICommandHandler<RegisterUserCommand, OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>>,
            RegisterUserCommandHandler>();

        services.AddScoped<
            ICommandHandler<LoginUserCommand, OneOf<TokenPair, InvalidCredentials>>,
            LoginUserCommandHandler>();

        services.AddScoped<
            ICommandHandler<RefreshSessionCommand, OneOf<TokenPair, InvalidOrExpired>>,
            RefreshSessionCommandHandler>();

        services.AddScoped<
            ICommandHandler<RevokeSessionCommand, OneOf<Success, NotFound>>,
            RevokeSessionCommandHandler>();

        services.AddScoped<
            IQueryHandler<GetCurrentUserQuery, OneOf<GetCurrentUserResult, NotFound>>,
            GetCurrentUserQueryHandler>();

        return services;
    }
}
