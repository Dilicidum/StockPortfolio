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

    /// <summary>Registers Identity's bearer tokens and the services behind MapIdentityApi.</summary>
    public static IServiceCollection AddStockPortfolioAuthentication(this IServiceCollection services)
    {
        // Configures the bearer scheme and the cookie schemes, and adds the endpoint services.
        // The EF store itself is registered by AddIdentityModule, which may not reference this assembly.
        services.AddIdentityApiEndpoints<IdentityUser>(options =>
            options.ClaimsIdentity.UserIdClaimType = UserIdClaimType);

        return services;
    }

    /// <summary>Maps register, login, refresh and the manage routes under /api/auth, plus logout.</summary>
    public static IEndpointRouteBuilder MapStockPortfolioAuthentication(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(AuthRoutePrefix).WithTags("Authentication");

        group.MapIdentityApi<IdentityUser>();

        // MapIdentityApi ships no logout route — verified against the endpoint list in Microsoft's own
        // "Identity to secure a Web API backend for SPAs". This is the version those docs give.
        //
        // With bearer tokens rather than cookies it clears the cookie schemes and nothing else: the
        // access and refresh tokens the caller holds stay valid until they expire. Ending a session
        // early is now the client discarding its tokens, not the server retiring them.
        group.MapPost("/logout", async (SignInManager<IdentityUser> signInManager) =>
            {
                await signInManager.SignOutAsync();

                return TypedResults.Ok();
            })
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Signs the caller out of the cookie schemes.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }
}
