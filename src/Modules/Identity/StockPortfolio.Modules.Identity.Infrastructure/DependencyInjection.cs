using Microsoft.Extensions.DependencyInjection;

using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Identity.Application;
using StockPortfolio.Modules.Identity.Application.Authentication;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;
using StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;
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
        // Not a handler, but the collaborator all three session-opening handlers share.
        services.AddScoped<SessionOpener>();

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

        services.AddScoped<IQueryHandler<GetAppearanceQuery, GetAppearanceResult>, GetAppearanceQueryHandler>();
        services.AddScoped<
            ICommandHandler<SaveAppearanceCommand, OneOf<GetAppearanceResult, InvalidInput>>,
            SaveAppearanceCommandHandler>();

        return services;
    }
}
