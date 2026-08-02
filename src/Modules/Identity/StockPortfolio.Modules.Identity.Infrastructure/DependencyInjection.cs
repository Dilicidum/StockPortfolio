using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Infrastructure;

/// <summary>Handler registrations, kept out of <see cref="IdentityModule"/> so the public seam stays one method.</summary>
/// <remarks>
/// <para>
/// Handlers are registered here rather than in <c>.Application</c> because this assembly owns the
/// concrete repositories, hasher and token issuer they depend on — putting the registrations next to the
/// interfaces would leave <c>.Application</c> describing a graph it cannot build.
/// </para>
/// <para>
/// Written out one line per handler instead of an assembly scan. There are five, the list is the
/// module's inventory, and a scan that silently registers nothing is a failure mode this project has
/// already paid for once (see the FluentValidation note in phase-1-implementation.md §4.4).
/// </para>
/// </remarks>
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
