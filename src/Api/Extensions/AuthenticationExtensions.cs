using System.Security.Claims;

using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;

namespace StockPortfolio.Api.Extensions;

/// <summary>ASP.NET Core Identity bearer-token authentication for the whole host.</summary>
internal static class AuthenticationExtensions
{
    /// <summary>Where the framework's own account routes are mounted.</summary>
    public const string AuthRoutePrefix = "/api/auth";

    /// <summary>
    /// The claim the user id travels under. Overriding the framework default of ClaimTypes.NameIdentifier
    /// is the one setting this host changes, and it is deliberate: `sub` is the JWT registered claim name
    /// (RFC 7519) that every external identity provider issues, so the other three modules read a portable
    /// name rather than a WS-Federation URI specific to ASP.NET Core Identity. UserManager.GetUserId reads
    /// this same option, so the framework follows the override rather than fighting it.
    /// </summary>
    public const string UserIdClaimType = "sub";

    /// <summary>How long an access token stays usable, and so how long a logout takes to fully bite.</summary>
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Registers Identity's bearer tokens and the services behind MapIdentityApi.</summary>
    public static IServiceCollection AddStockPortfolioAuthentication(this IServiceCollection services)
    {
        // Configures the bearer scheme and the cookie schemes, and adds the endpoint services.
        // The EF store itself is registered by AddIdentityModule, which may not reference this assembly.
        services.AddIdentityApiEndpoints<IdentityUser>(options =>
            options.ClaimsIdentity.UserIdClaimType = UserIdClaimType);

        // The access token is the one window logout cannot close: it is a self-contained payload,
        // validated by decrypting it and never by asking the database, so nothing can retire it early.
        // The default is one hour. Fifteen minutes is what this app issued before the migration, and it
        // is the whole of the residual risk after logout — see MapStockPortfolioAuthentication.
        services.Configure<BearerTokenOptions>(
            IdentityConstants.BearerScheme,
            options => options.BearerTokenExpiration = AccessTokenLifetime);

        return services;
    }

    /// <summary>Maps register, login, refresh and the manage routes under /api/auth, plus logout.</summary>
    public static IEndpointRouteBuilder MapStockPortfolioAuthentication(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(AuthRoutePrefix).WithTags("Authentication");

        group.MapIdentityApi<IdentityUser>();

        // MapIdentityApi ships no logout route, so this one is hand-written. Microsoft's SPA guidance
        // gives a version that only calls SignOutAsync; that clears the cookie schemes and leaves a
        // bearer caller's refresh token working for its full fourteen days, which is not a logout.
        //
        // Rolling the security stamp is what actually revokes. MapIdentityApi's /refresh calls
        // ValidateSecurityStampAsync before issuing anything, so every refresh token this user holds
        // stops working on its next use — no blocklist, no session table, no per-request database read.
        //
        // What it does NOT close is the access token, which is validated by decryption alone and never
        // against the database. That window is BearerTokenExpiration, held at 15 minutes above.
        //
        // Known and deliberate: the stamp is per USER, so this is log-out-everywhere. The model it
        // replaced could retire one session and leave a second device signed in. Per-device logout
        // needs the session table this migration deleted; getting it back means Option B or C, not a
        // setting. Say so before anyone reads this route as equivalent to the old one.
        group.MapPost("/logout", async (
                SignInManager<IdentityUser> signInManager,
                UserManager<IdentityUser> userManager,
                ClaimsPrincipal principal) =>
            {
                // Reads the same UserIdClaimType configured above, so it follows the `sub` override.
                var user = await userManager.GetUserAsync(principal);

                if (user is not null)
                {
                    await userManager.UpdateSecurityStampAsync(user);
                }

                await signInManager.SignOutAsync();

                return TypedResults.Ok();
            })
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Ends the session: revokes every refresh token this user holds.")
            .WithDescription(
                "The access token already issued stays usable for up to 15 minutes; refresh stops "
                + "immediately. Logs the user out on every device, not only this one.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}
