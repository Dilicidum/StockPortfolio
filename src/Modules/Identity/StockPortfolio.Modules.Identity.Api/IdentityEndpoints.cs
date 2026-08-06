using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using StockPortfolio.Modules.Identity.Api.Requests;
using StockPortfolio.Modules.Identity.Api.Validators;
using StockPortfolio.Modules.Identity.Application.Preferences.Commands.SaveAppearance;
using StockPortfolio.Modules.Identity.Application.Preferences.Queries.GetAppearance;
using StockPortfolio.Shared.Api;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Api;

/// <summary>What is left of this module's HTTP surface: two settings routes, plus the one DI seam.</summary>
/// <remarks>
/// Register, login and refresh are gone from here. MapIdentityApi supplies them, and the host maps it.
/// </remarks>
public static class IdentityEndpoints
{
    /// <summary>Written by UserClaimsPrincipalFactory, from IdentityOptions.ClaimsIdentity.UserIdClaimType.</summary>
    private const string SubjectClaimType = "sub";

    /// <summary>Registers the module's presentation-layer services: the request validators.</summary>
    public static IServiceCollection AddIdentityApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<SaveAppearanceRequestValidator>();

        return services;
    }

    /// <summary>Maps the two settings routes. The framework's own endpoints are mapped by the host.</summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
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

        return result.Match<IResult>(
            saved => TypedResults.Ok(saved),

            // Reachable only if the validator and the handler disagree about the allowed set.
            invalid => invalid.ToValidationProblem());
    }

    // A string, not a Guid: IdentityUser.Id is a string, and the framework's own UserManager.GetUserId
    // reads this same claim as one. Parsing it to Guid here would work today only because the default
    // id happens to be a Guid, and would break the moment a user is created with any other id.
    private static bool TryReadUserId(ClaimsPrincipal principal, out string userId)
    {
        userId = principal.FindFirstValue(SubjectClaimType) ?? string.Empty;

        return userId.Length > 0;
    }
}
