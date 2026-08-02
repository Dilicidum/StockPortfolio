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

/// <summary>
/// The Identity module's entire inbound HTTP surface: five routes under <c>/api/auth</c>, plus the
/// one DI call they need.
/// </summary>
/// <remarks>
/// <para>
/// Handlers are injected straight into the endpoint methods as <see cref="ICommandHandler{T, R}"/>
/// and <see cref="IQueryHandler{T, R}"/>. There is no dispatcher and no mediator: there is exactly
/// one caller per handler, so a mediator would have nothing to decouple.
/// </para>
/// <para>
/// Every multi-case method carries an explicit <c>Results&lt;…&gt;</c> return type.
/// <c>TypedResults.Ok(x)</c> and <c>TypedResults.Problem(…)</c> are unrelated types with no common
/// base; without the annotation the compiler abandons the inferred delegate, falls back to
/// matching <c>RequestDelegate(HttpContext)</c>, and reports <c>CS1593: delegate does not take N
/// arguments</c> — which points at the parameter list rather than the return type.
/// </para>
/// </remarks>
public static class IdentityEndpoints
{
    /// <summary>
    /// Where a newly created account is addressable. Used as the <c>Location</c> of the
    /// <c>201</c> from register.
    /// </summary>
    /// <remarks>
    /// A <c>201</c> is supposed to say where the thing it created now lives. There is no
    /// <c>GET /api/users/{id}</c> — users are not a browsable collection in this application, and
    /// adding one purely to satisfy the header would be a real endpoint with real authorisation
    /// questions built to decorate a response. The created resource <i>is</i> addressable, but
    /// only as the caller's own identity, so that is what the header points at. A bare 201 with no
    /// Location would read as an oversight rather than a decision.
    /// </remarks>
    private const string CurrentUserPath = "/api/auth/me";

    /// <summary>
    /// The claim carrying the user id.
    /// </summary>
    /// <remarks>
    /// Read as the literal <c>"sub"</c> because the host sets
    /// <c>JwtBearerOptions.MapInboundClaims = false</c>. With the default of <see langword="true"/>
    /// the handler renames <c>sub</c> to the long <c>ClaimTypes.NameIdentifier</c> URI and a lookup
    /// for <c>"sub"</c> silently returns null — a 401 on every authenticated request, with nothing
    /// in the logs to explain it.
    /// </remarks>
    private const string SubjectClaimType = "sub";

    /// <summary>
    /// Registers the module's presentation-layer services: the request validators.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Scanning this assembly picks up every <c>AbstractValidator&lt;T&gt;</c> in one call, so
    /// adding a request record and its validator never means editing the host.
    /// <see cref="ValidationFilter{T}"/> injects <c>IValidator&lt;T&gt;</c> singly rather than as a
    /// collection, so a validator that was never registered fails loudly at the first request
    /// instead of quietly validating nothing.
    /// </remarks>
    public static IServiceCollection AddIdentityApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddValidatorsFromAssemblyContaining<LoginUserCommandValidator>();

        return services;
    }

    /// <summary>
    /// Maps the five authentication routes onto <c>/api/auth</c>.
    /// </summary>
    /// <param name="app">The route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <remarks>
    /// Only the three routes that carry a body get a <see cref="ValidationFilter{T}"/>.
    /// <c>/logout</c> and <c>/me</c> take nothing but a bearer token, so there is nothing to
    /// validate and no empty validator is invented for symmetry.
    /// </remarks>
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
            .WithName("GetCurrentUserQuery")
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

            // The handler's own ValidationFailed case, not the filter's. The filter has already
            // passed by the time we get here; this is a rule the domain enforces that the request
            // shape cannot express.
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
        // No body, or a body with no token: nothing is revocable. The access token is short-lived
        // and self-contained, so there is nothing to invalidate server-side either. 204 is honest.
        if (string.IsNullOrWhiteSpace(command?.RefreshToken))
        {
            return TypedResults.NoContent();
        }

        var result = await handler
            .Handle(command, ct)
            .ConfigureAwait(false);

        // Both cases are 204. Sign-out is idempotent, and answering 404 for an unknown token would
        // turn the endpoint into an oracle for which token strings exist.
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
        // A token that authenticated but carries no usable `sub` is a broken token, not a broken
        // user — 401 so the client refreshes rather than retrying the same credential forever.
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
