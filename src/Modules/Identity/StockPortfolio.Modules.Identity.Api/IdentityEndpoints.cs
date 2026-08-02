using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using OneOf.Types;
using StockPortfolio.Modules.Identity.Api.Requests;
using StockPortfolio.Modules.Identity.Api.Validators;
using StockPortfolio.Modules.Identity.Application;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;
using StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;
using StockPortfolio.Shared.Api;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Api;

/// <summary>The Identity module's entire inbound HTTP surface: five routes under /api/auth, plus the one DI seam.</summary>
public static class IdentityEndpoints
{
    /// <summary>Where a newly created account is addressable.</summary>
    private const string CurrentUserPath = "/api/auth/me";

    /// <summary>The claim carrying the user id.</summary>
    private const string SubjectClaimType = "sub";

    /// <summary>Registers the module's presentation-layer services: the request validators.</summary>
    public static IServiceCollection AddIdentityApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<LoginUserRequestValidator>();

        return services;
    }

    /// <summary>Maps the five authentication routes onto /api/auth.</summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        // Every status an endpoint can actually emit is declared. 415 and 500 carry problem+json
        // because AddProblemDetails and UseStatusCodePages give even framework-generated
        // responses a body - verified against the running API, not assumed.

        // 500 is the one status every route here shares, so it is declared once on the group.
        var group = app.MapGroup("/api/auth")
            .WithTags("Authentication")
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapPost("/register", RegisterAsync)
            .AddEndpointFilter<ValidationFilter<RegisterUserRequest>>()
            .AllowAnonymous()
            .WithName("Register")
            .WithSummary("Creates an account and signs the caller straight in.")
            .WithDescription("Returns the same token pair as login; Location points at /api/auth/me.")
            .Produces<TokenPair>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPost("/login", LoginAsync)
            .AddEndpointFilter<ValidationFilter<LoginUserRequest>>()
            .AllowAnonymous()
            .WithName("Login")
            .WithSummary("Exchanges email and password for a token pair.")
            .WithDescription("A wrong password and an unknown email give the identical 401, so the endpoint is not an account enumerator.")
            .Produces<TokenPair>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPost("/refresh", RefreshAsync)
            .AddEndpointFilter<ValidationFilter<RefreshSessionRequest>>()
            .AllowAnonymous()
            .WithName("Refresh")
            .WithSummary("Exchanges a refresh token for a fresh token pair.")
            .WithDescription("Anonymous by design: the caller arrives here because its access token has expired.")
            .Produces<TokenPair>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Ends the session.")
            .WithDescription("Idempotent. Send the refresh token in the body to retire it immediately; omit it and the call still returns 204.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithSummary("Returns the identity behind the current access token.")
            .Produces<GetCurrentUserResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary>Creates an account and issues its first token pair.</summary>
    private static async Task<IResult> RegisterAsync(
        RegisterUserRequest request,
        ICommandHandler<RegisterUserCommand, OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new RegisterUserCommand(request.Email, request.Password), ct);

        return result.Match<IResult>(
            tokens => TypedResults.Created(CurrentUserPath, tokens),
            emailTaken => ProblemDetailsExtensions.ConflictProblem("An account with that email address already exists."),

            // The handler's own InvalidInput case, not the filter's.
            invalid => invalid.ToValidationProblem());
    }

    /// <summary>Signs an existing account in.</summary>
    private static async Task<IResult> LoginAsync(
        LoginUserRequest request,
        ICommandHandler<LoginUserCommand, OneOf<TokenPair, InvalidCredentials>> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new LoginUserCommand(request.Email, request.Password), ct);

        return result.Match<IResult>(
            tokens => TypedResults.Ok(tokens),
            rejected => ProblemDetailsExtensions.UnauthorizedProblem("Invalid credentials."));
    }

    /// <summary>Rotates a refresh token into a new pair.</summary>
    private static async Task<IResult> RefreshAsync(
        RefreshSessionRequest request,
        ICommandHandler<RefreshSessionCommand, OneOf<TokenPair, InvalidOrExpired>> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new RefreshSessionCommand(request.RefreshToken), ct);

        return result.Match<IResult>(
            tokens => TypedResults.Ok(tokens),
            rejected => ProblemDetailsExtensions.UnauthorizedProblem("That refresh token is not valid."));
    }

    /// <summary>Ends the session, revoking the refresh token when one is offered.</summary>
    private static async Task<IResult> LogoutAsync(
        RevokeSessionRequest? request,
        ICommandHandler<RevokeSessionCommand, OneOf<Success, NotFound>> handler,
        CancellationToken ct)
    {
        // No body, or a body with no token: nothing is revocable.
        if (request is null || string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return TypedResults.NoContent();
        }

        var result = await handler.Handle(new RevokeSessionCommand(request.RefreshToken), ct);

        // Both cases are 204: logging out twice is not an error.
        return result.Match<IResult>(
            closed => TypedResults.NoContent(),
            nothingToClose => TypedResults.NoContent());
    }

    /// <summary>Resolves the bearer token back to a user.</summary>
    private static async Task<IResult> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        IQueryHandler<GetCurrentUserQuery, OneOf<GetCurrentUserResult, NotFound>> handler,
        CancellationToken ct)
    {
        // Totality over a string?, not a security control: OnTokenValidated already rejects a subject-less
        // token, so this only gives FindFirstValue's null a branch to go down.
        if (!Guid.TryParse(principal.FindFirstValue(SubjectClaimType), out var userId))
        {
            return ProblemDetailsExtensions.UnauthorizedProblem("The access token carries no usable subject.");
        }

        var result = await handler.Handle(new GetCurrentUserQuery(userId), ct);

        return result.Match<IResult>(
            user => TypedResults.Ok(user),

            // The JWT outlived the account it names — deleted, or issued by a previous database.
            gone => ProblemDetailsExtensions.UnauthorizedProblem("This session no longer refers to a valid account."));
    }
}
