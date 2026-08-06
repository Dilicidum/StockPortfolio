using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;

using StockPortfolio.Modules.Alerts.Api;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Api.Extensions;

/// <summary>ASP.NET Core Identity bearer-token authentication for the whole host.</summary>
/// <remarks>
/// Every override here is deliberate and none of them is cosmetic. The framework's defaults are written
/// for an app whose username is a username; this app's username is an email, and its password policy is
/// length-based. Taking those two defaults unchanged rejects ordinary addresses and ordinary passphrases.
/// </remarks>
internal static class AuthenticationExtensions
{
    /// <summary>Where this app's own account routes are mounted.</summary>
    public const string AuthRoutePrefix = "/api/auth";

    /// <summary>
    /// The claim the user id travels under. `sub` is the JWT registered claim name (RFC 7519) that every
    /// external identity provider issues, so the other three modules read a portable name rather than a
    /// WS-Federation URI specific to ASP.NET Core Identity. UserManager.GetUserId reads this same option,
    /// so the framework follows the override rather than fighting it.
    /// </summary>
    public const string UserIdClaimType = "sub";

    /// <summary>How long an access token stays usable, and so how long a logout takes to fully bite.</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Registers Identity's bearer tokens, the sign-in manager, and this app's option overrides.</summary>
    public static IServiceCollection AddStockPortfolioAuthentication(this IServiceCollection services)
    {
        // AddIdentityApiEndpoints wires the bearer scheme, the cookie schemes and SignInManager. The
        // endpoints it enables are NOT mapped: this app maps its own, in the Identity module's .Api.
        // The EF store itself is registered by AddIdentityModule, which may not reference this assembly.
        services.AddIdentityApiEndpoints<AppUser>(options =>
        {
            options.ClaimsIdentity.UserIdClaimType = UserIdClaimType;

            // Length is the strength. The default demands a digit, an uppercase, a lowercase and a
            // symbol, which pushes users towards "Passw0rd!" and rejects a long passphrase outright.
            options.Password.RequiredLength = 12;
            options.Password.RequireDigit = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireNonAlphanumeric = false;

            // The username IS the email here. The default allowed set is letters, digits and a handful
            // of symbols, so a perfectly ordinary address containing an apostrophe — o'brien@x.test —
            // is refused with "Username is invalid, can only contain letters or digits". Empty means
            // the character check is skipped entirely, which is the documented way to switch it off.
            options.User.AllowedUserNameCharacters = string.Empty;
            options.User.RequireUniqueEmail = true;
        });

        // The access token is the one window logout cannot close: it is a self-contained payload,
        // validated by decrypting it and never by asking the database, so nothing can retire it early.
        // The default is one hour; 15 minutes is what this app issued before the migration, and it is
        // the whole of the residual risk after a logout.
        services.Configure<BearerTokenOptions>(
            IdentityConstants.BearerScheme,
            options =>
            {
                options.BearerTokenExpiration = AccessTokenLifetime;
                options.Events = new BearerTokenEvents { OnMessageReceived = ReadTokenFromHubQuery };
            });

        return services;
    }

    /// <summary>Lets the alert hub authenticate from the query string, which is the only place it can.</summary>
    private static Task ReadTokenFromHubQuery(MessageReceivedContext context)
    {
        // A browser cannot put a header on a streaming connection, so SignalR's JavaScript client
        // sends the token as ?access_token= instead. Without this the hub sees an anonymous request
        // and rejects every connection.
        var token = context.Request.Query["access_token"].ToString();

        // Path-scoped deliberately: without the check, ANY route in the app could be authenticated by
        // query string, which puts the token in every access log for the sake of one connection.
        if (!string.IsNullOrEmpty(token)
            && context.Request.Path.StartsWithSegments(AlertsEndpoints.HubPath))
        {
            context.Token = token;
        }

        return Task.CompletedTask;
    }
}
