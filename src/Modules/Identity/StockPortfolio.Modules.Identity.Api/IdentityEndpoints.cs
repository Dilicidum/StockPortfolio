using System.Security.Claims;

using FluentValidation;

using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using OneOf;

using StockPortfolio.Modules.Identity.Api.Requests;
using StockPortfolio.Modules.Identity.Api.Validators;
using StockPortfolio.Modules.Identity.Application.Preferences.Commands.SaveAppearance;
using StockPortfolio.Modules.Identity.Application.Preferences.Queries.GetAppearance;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Api;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Api;

/// <summary>The Identity module's inbound HTTP surface: five routes under /api/auth, two under /api/settings.</summary>
/// <remarks>
/// These are hand-written over UserManager and SignInManager rather than mapped from MapIdentityApi.
/// The library still does every hard thing — password hashing, lockout, the security stamp, the token
/// protectors. What is not delegated is the wire contract: MapIdentityApi answers register with an empty
/// 200, reports a duplicate address as a 400, and offers no /me. Owning ~5 thin endpoints buys the shapes
/// this app's SPA and its OpenAPI document already describe, and adds no authentication logic.
/// </remarks>
public static class IdentityEndpoints
{
    /// <summary>Written by UserClaimsPrincipalFactory, from IdentityOptions.ClaimsIdentity.UserIdClaimType.</summary>
    private const string SubjectClaimType = "sub";

    /// <summary>Where a newly created account is addressable, for register's Location header.</summary>
    private const string CurrentUserPath = "/api/auth/me";

    /// <summary>Registers the module's presentation-layer services: the request validators.</summary>
    public static IServiceCollection AddIdentityApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<RegisterUserRequestValidator>();

        return services;
    }

    /// <summary>Maps the five authentication routes and the two settings routes.</summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication")
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/register", RegisterAsync)
            .AddEndpointFilter<ValidationFilter<RegisterUserRequest>>()
            .AllowAnonymous()
            .WithName("Register")
            .WithSummary("Creates an account and signs the caller straight in.")
            .WithDescription("Returns the same token pair as login; Location points at /api/auth/me.")
            .Produces<AccessTokenResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPost("/login", LoginAsync)
            .AddEndpointFilter<ValidationFilter<LoginUserRequest>>()
            .AllowAnonymous()
            .WithName("Login")
            .WithSummary("Exchanges email and password for a token pair.")
            .WithDescription("A wrong password and an unknown email give the identical 401.")
            .Produces<AccessTokenResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPost("/refresh", RefreshAsync)
            .AddEndpointFilter<ValidationFilter<RefreshSessionRequest>>()
            .AllowAnonymous()
            .WithName("Refresh")
            .WithSummary("Exchanges a refresh token for a fresh token pair.")
            .WithDescription("Anonymous by design: the caller arrives because its access token has expired.")
            .Produces<AccessTokenResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Ends the session: revokes every refresh token this user holds.")
            .WithDescription(
                "The access token already issued stays usable until it expires; refresh stops at once. "
                + "Signs the user out on every device, because the security stamp is per account.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithSummary("Returns the identity behind the current access token.")
            .Produces<CurrentUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        var settings = app.MapGroup("/api/settings")
            .WithTags("Settings")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        settings.MapGet("/appearance", GetAppearanceAsync)
            .WithName("GetAppearance")
            .WithSummary("Returns the caller's theme and language, defaulting to system and English.")
            .Produces<GetAppearanceResult>(StatusCodes.Status200OK);

        settings.MapPut("/appearance", SaveAppearanceAsync)
            .AddEndpointFilter<ValidationFilter<SaveAppearanceRequest>>()
            .WithName("SaveAppearance")
            .WithSummary("Saves the caller's theme and language.")
            .Produces<GetAppearanceResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        return app;
    }

    /// <summary>Creates an account and issues its first token pair.</summary>
    private static async Task<IResult> RegisterAsync(
        RegisterUserRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        HttpContext http)
    {
        var email = request.Email.Trim();

        // "Is this address taken?" is a context question, so it is asked here and answered as a 409.
        // CreateAsync would report it too, but as a 400 named DuplicateUserName, which is a worse answer.
        if (await users.FindByEmailAsync(email) is not null)
        {
            return ProblemDetailsExtensions.ConflictProblem("An account with that email address already exists.");
        }

        var user = new AppUser { UserName = email, Email = email };
        var created = await users.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            return ValidationProblemFrom(created);
        }

        // Set BEFORE the sign-in, and this is the whole trick. BearerTokenHandler.HandleSignInAsync
        // writes the token body with WriteAsJsonAsync and never assigns a status code, so whatever is
        // set here survives and the pair goes out under a 201 with a Location.
        //
        // It cannot be done afterwards. SignInAsync writes the entire response, so returning
        // TypedResults.Created below would be setting a status on a response that has already started.
        // TypedResults.Empty is the "already handled, leave it alone" result.
        http.Response.StatusCode = StatusCodes.Status201Created;
        http.Response.Headers.Location = CurrentUserPath;

        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;
        await signIn.SignInAsync(user, isPersistent: false);

        return TypedResults.Empty;
    }

    /// <summary>Signs an existing account in.</summary>
    private static async Task<IResult> LoginAsync(
        LoginUserRequest request,
        SignInManager<AppUser> signIn)
    {
        signIn.AuthenticationScheme = IdentityConstants.BearerScheme;

        // lockoutOnFailure: true is the whole reason to go through SignInManager rather than
        // CheckPasswordAsync. It is also the brute-force protection the hand-written module never had.
        var result = await signIn.PasswordSignInAsync(
            request.Email.Trim(), request.Password, isPersistent: false, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return ProblemDetailsExtensions.UnauthorizedProblem(
                "Too many failed attempts. Try again later.");
        }

        if (!result.Succeeded)
        {
            // One answer for a wrong password and for an unknown address, so this is not an enumerator.
            return ProblemDetailsExtensions.UnauthorizedProblem("Invalid credentials.");
        }

        // PasswordSignInAsync already wrote the token pair on the bearer scheme.
        return TypedResults.Empty;
    }

    /// <summary>Exchanges a refresh token for a fresh pair.</summary>
    private static async Task<IResult> RefreshAsync(
        RefreshSessionRequest request,
        SignInManager<AppUser> signIn,
        IOptionsMonitor<BearerTokenOptions> bearerOptions,
        TimeProvider clock)
    {
        var protector = bearerOptions.Get(IdentityConstants.BearerScheme).RefreshTokenProtector;
        var ticket = protector.Unprotect(request.RefreshToken);

        // Three ways to fail, one answer: unreadable, expired, or the security stamp has moved on.
        // The last is what makes logout revoke — it is the only revocation the framework offers.
        if (ticket?.Properties?.ExpiresUtc is not { } expiresUtc
            || clock.GetUtcNow() >= expiresUtc
            || await signIn.ValidateSecurityStampAsync(ticket.Principal) is not AppUser user)
        {
            return ProblemDetailsExtensions.UnauthorizedProblem("That refresh token is not valid.");
        }

        return TypedResults.SignIn(
            await signIn.CreateUserPrincipalAsync(user),
            authenticationScheme: IdentityConstants.BearerScheme);
    }

    /// <summary>Ends the session by rolling the security stamp, which retires every refresh token.</summary>
    private static async Task<IResult> LogoutAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        // Reads the same UserIdClaimType the host configured, so it follows the `sub` override.
        if (await users.GetUserAsync(principal) is { } user)
        {
            // Refresh validates the stamp, so moving it stops every refresh token this user holds.
            // Without this line logout only clears cookies and a bearer caller stays signed in.
            await users.UpdateSecurityStampAsync(user);
        }

        await signIn.SignOutAsync();

        return TypedResults.NoContent();
    }

    /// <summary>Resolves the bearer token back to a user.</summary>
    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users)
    {
        if (await users.GetUserAsync(principal) is not { } user)
        {
            // The token outlived the account it names — deleted, or issued by a previous database.
            return ProblemDetailsExtensions.UnauthorizedProblem(
                "This session no longer refers to a valid account.");
        }

        return TypedResults.Ok(new CurrentUserResponse(user.Id, user.Email ?? string.Empty));
    }

    // Reads the caller's appearance settings, creating the default row on first read.
    private static async Task<IResult> GetAppearanceAsync(
        ClaimsPrincipal principal,
        IQueryHandler<GetAppearanceQuery, GetAppearanceResult> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId))
        {
            return ProblemDetailsExtensions.UnauthorizedProblem("The access token carries no usable subject.");
        }

        var result = await handler.Handle(new GetAppearanceQuery(userId), ct);

        return TypedResults.Ok(result);
    }

    // Saves the caller's appearance settings.
    private static async Task<IResult> SaveAppearanceAsync(
        SaveAppearanceRequest request,
        ClaimsPrincipal principal,
        ICommandHandler<SaveAppearanceCommand, OneOf<GetAppearanceResult, InvalidInput>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId))
        {
            return ProblemDetailsExtensions.UnauthorizedProblem("The access token carries no usable subject.");
        }

        var result = await handler.Handle(
            new SaveAppearanceCommand(userId, request.Theme, request.Language), ct);

        return result.Match(
            saved => Results.Ok(saved),

            // Reachable only if the validator and the handler disagree about the allowed set.
            invalid => invalid.ToValidationProblem());
    }

    private static bool TryReadUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(SubjectClaimType), out userId);

    // Identity reports every rule it rejected, so the 400 names them rather than saying "invalid".
    private static Microsoft.AspNetCore.Http.HttpResults.ValidationProblem ValidationProblemFrom(
        IdentityResult result) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Password"] = [.. result.Errors.Select(error => error.Description)],
        });
}

/// <summary>The body of GET /api/auth/me.</summary>
public sealed record CurrentUserResponse(Guid Id, string Email);
