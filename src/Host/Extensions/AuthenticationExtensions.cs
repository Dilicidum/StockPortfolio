using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;

using StockPortfolio.Modules.Alerts.Api;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Host.Extensions;

internal static class AuthenticationExtensions
{
    public const string AuthRoutePrefix = "/api/auth";

    /// <summary>`sub` is the RFC 7519 registered claim every provider issues, so the other modules read a portable name rather than ASP.NET Core Identity's WS-Federation URI.</summary>
    public const string UserIdClaimType = "sub";

    /// <summary>A bearer token is self-contained and cannot be retired early, so this 15 minutes is the whole residual window after a logout.</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    public static IServiceCollection AddStockPortfolioAuthentication(this IServiceCollection services)
    {
        // Wires the schemes and SignInManager only; the endpoints it enables are never mapped, because the Identity module maps this app's own.
        services.AddIdentityApiEndpoints<AppUser>(options =>
        {
            options.ClaimsIdentity.UserIdClaimType = UserIdClaimType;

            // Length is the strength: the default four-class rule pushes users towards "Passw0rd!" and rejects a long passphrase.
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;

            // The username is the email, and the default character set refuses o'brien@x.test; empty switches the check off entirely.
            options.User.AllowedUserNameCharacters = string.Empty;
            options.User.RequireUniqueEmail = true;
        });

        services.Configure<BearerTokenOptions>(
            IdentityConstants.BearerScheme,
            options =>
            {
                options.BearerTokenExpiration = AccessTokenLifetime;
                options.Events = new BearerTokenEvents { OnMessageReceived = ReadTokenFromHubQuery };
            });

        return services;
    }

    /// <summary>A browser cannot set a header on the hub connection, so SignalR sends the token as ?access_token=; the path check stops every other route accepting one.</summary>
    private static Task ReadTokenFromHubQuery(MessageReceivedContext context)
    {
        var token = context.Request.Query["access_token"].ToString();

        if (!string.IsNullOrEmpty(token)
            && context.Request.Path.StartsWithSegments(AlertsEndpoints.HubPath))
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    }
}
