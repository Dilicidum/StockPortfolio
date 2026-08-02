using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StockPortfolio.Modules.Identity.Application;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;
using StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;
using StockPortfolio.Modules.Identity.Api.Validators;
using StockPortfolio.Shared.Kernel.Cqrs;
using StockPortfolio.Shared.Api;

namespace StockPortfolio.Modules.Identity.Api;

/// <summary>The Identity module's entire inbound HTTP surface: five routes under /api/auth, plus the one DI.</summary>
public static class IdentityEndpoints
{
    /// <summary>Where a newly created account is addressable.</summary>
    private const string CurrentUserPath = "/api/auth/me";

    /// <summary>The claim carrying the user id.</summary>
    private const string SubjectClaimType = "sub";

    /// <summary>Registers the module's presentation-layer services: the request validators.</summary>
    public static IServiceCollection AddIdentityApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidatorsFromAssemblyContaining<LoginUserCommandValidator>();

        return services;
    }

    /// <summary>Maps the five authentication routes onto /api/auth.</summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/register", RegisterAsync)
            .AddEndpointFilter<ValidationFilter<RegisterUserCommand>>()
            .AllowAnonymous()
            .WithName("Register")
            .WithSummary("Creates an account and signs the caller straight in.")
            .WithDescription(
                "Returns the same token pair as login, so the SPA never has to post the password " +
                "twice. Location points at /api/auth/me, the only address the new account has.")
            .Produces<TokenPair>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/login", LoginAsync)
            .AddEndpointFilter<ValidationFilter<LoginUserCommand>>()
            .AllowAnonymous()
            .WithName("Login")
            .WithSummary("Exchanges email and password for a token pair.")
            .WithDescription(
                "A wrong password and an unknown email give the identical 401. The distinction is " +
                "withheld on purpose: telling them apart turns the endpoint into an account enumerator.")
            .Produces<TokenPair>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .AddEndpointFilter<ValidationFilter<RefreshSessionCommand>>()
            .AllowAnonymous()
            .WithName("Refresh")
            .WithSummary("Exchanges a refresh token for a fresh token pair.")
            .WithDescription(
                "Anonymous by design — the caller reaches here precisely because its access token " +
                "has expired, so requiring a valid bearer would make the endpoint unreachable.")
            .Produces<TokenPair>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .RequireAuthorization()
            .WithName("Logout")
            .WithSummary("Ends the session.")
            .WithDescription(
                "Idempotent: 204 whether or not a refresh token was supplied and whether or not it " +
                "was still live. Send the refresh token in the body to retire it immediately.")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .WithName("GetCurrentUser")
            .WithSummary("Returns the identity behind the current access token.")
            .Produces<UserSummary>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        return app;
    }

    /// <summary>Creates an account and issues its first token pair.</summary>
    private static async Task<Results<Created<TokenPair>, ProblemHttpResult, ValidationProblem>> RegisterAsync(
        RegisterUserCommand command,
        ICommandHandler<RegisterUserCommand, RegisterUserResult> handler,
        CancellationToken ct)
    {
        var result = await handler
            .Handle(command, ct)
            .ConfigureAwait(false);

        return result.Match<Results<Created<TokenPair>, ProblemHttpResult, ValidationProblem>>(
            tokens => TypedResults.Created(CurrentUserPath, tokens),
            _ => ProblemDetailsExtensions.ConflictProblem("An account with that email address already exists."),

            // The handler's own ValidationFailed case, not the filter's.
            failure => failure.ToValidationProblem());
    }

    /// <summary>Signs an existing account in.</summary>
    private static async Task<Results<Ok<TokenPair>, ProblemHttpResult>> LoginAsync(
        LoginUserCommand command,
        ICommandHandler<LoginUserCommand, LoginUserResult> handler,
        CancellationToken ct)
    {
        var result = await handler
            .Handle(command, ct)
            .ConfigureAwait(false);

        return result.Match<Results<Ok<TokenPair>, ProblemHttpResult>>(
            tokens => TypedResults.Ok(tokens),
            _ => ProblemDetailsExtensions.UnauthorizedProblem("Invalid credentials."));
    }

    /// <summary>Rotates a refresh token into a new pair.</summary>
    private static async Task<Results<Ok<TokenPair>, ProblemHttpResult>> RefreshAsync(
        RefreshSessionCommand command,
        ICommandHandler<RefreshSessionCommand, RefreshSessionResult> handler,
        CancellationToken ct)
    {
        var result = await handler
            .Handle(command, ct)
            .ConfigureAwait(false);

        return result.Match<Results<Ok<TokenPair>, ProblemHttpResult>>(
            tokens => TypedResults.Ok(tokens),
            _ => ProblemDetailsExtensions.UnauthorizedProblem("That refresh token is not valid."));
    }

    /// <summary>Ends the session, revoking the refresh token when one is offered.</summary>
    private static async Task<NoContent> LogoutAsync(
        RevokeSessionCommand? command,
        ICommandHandler<RevokeSessionCommand, RevokeSessionResult> handler,
        CancellationToken ct)
    {
        // No body, or a body with no token: nothing is revocable.
        if (command is null || string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return TypedResults.NoContent();
        }

        var result = await handler
            .Handle(command, ct)
            .ConfigureAwait(false);

        // Both cases are 204.
        return result.Match(
            _ => TypedResults.NoContent(),
            _ => TypedResults.NoContent());
    }

    /// <summary>Resolves the bearer token back to a user.</summary>
    private static async Task<Results<Ok<UserSummary>, ProblemHttpResult>> GetCurrentUserAsync(
        ClaimsPrincipal principal,
        IQueryHandler<GetCurrentUserQuery, GetCurrentUserResult> handler,
        CancellationToken ct)
    {
        // A token that authenticated but carries no usable `sub` is a broken token, not a broken user — 401.
        if (!Guid.TryParse(principal.FindFirstValue(SubjectClaimType), out var userId))
        {
            return ProblemDetailsExtensions.UnauthorizedProblem("The access token carries no usable subject.");
        }

        var result = await handler
            .Handle(new GetCurrentUserQuery(userId), ct)
            .ConfigureAwait(false);

        return result.Match<Results<Ok<UserSummary>, ProblemHttpResult>>(
            user => TypedResults.Ok(user),

            // The JWT outlived the account it names — deleted, or issued by a previous database.
            _ => ProblemDetailsExtensions.UnauthorizedProblem("This session no longer refers to a valid account."));
    }

}
