using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OneOf;
using OneOf.Types;
using StockPortfolio.Modules.Portfolio.Api.Requests;
using StockPortfolio.Modules.Portfolio.Api.Validators;
using StockPortfolio.Modules.Portfolio.Application;
using StockPortfolio.Modules.Portfolio.Application.Dashboard.Commands.SaveDashboardSettings;
using StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboard;
using StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboardSettings;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.AddHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.RemoveHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.SetHoldingVisibility;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Commands.UpdateHolding;
using StockPortfolio.Modules.Portfolio.Application.Holdings.Queries.GetHoldings;
using StockPortfolio.Shared.Api;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Api;

/// <summary>The Portfolio module's entire inbound HTTP surface: five routes under /api/holdings, the dashboard, two under /api/settings, and the one DI seam.</summary>
public static class PortfolioEndpoints
{
    /// <summary>Where a position is addressable.</summary>
    private const string BasePath = "/api/holdings";

    /// <summary>The dashboard is a portfolio read that happens to need prices, so it is Portfolio's route.</summary>
    private const string DashboardPath = "/api/dashboard";

    /// <summary>The claim carrying the user id.</summary>
    private const string SubjectClaimType = "sub";

    /// <summary>Registers the module's presentation-layer services: the request validators.</summary>
    public static IServiceCollection AddPortfolioApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<AddHoldingRequestValidator>();

        return services;
    }

    /// <summary>Maps the five holdings routes onto /api/holdings, the dashboard, and the /api/settings/dashboard pair.</summary>
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        // Every route needs a bearer token and every route can 500, so both are declared once here.
        // 415 and 500 carry problem+json because AddProblemDetails and UseStatusCodePages give even
        // framework-generated responses a body - verified against the running API, not assumed.
        var group = app.MapGroup(BasePath)
            .RequireAuthorization()
            .WithTags("Holdings")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/", GetHoldingsAsync)
            .WithName("GetHoldings")
            .WithSummary("Lists every position the caller holds.")
            .Produces<IReadOnlyList<HoldingSummary>>(StatusCodes.Status200OK);

        group.MapPost("/", AddHoldingAsync)
            .AddEndpointFilter<ValidationFilter<AddHoldingRequest>>()
            .WithName("AddHolding")
            .WithSummary("Records a purchase, opening a position or merging into an existing one.")
            .WithDescription("201 when the position is new, 200 when the purchase merged into one you already held. Location points at the position on the 201.")
            .Produces<HoldingSummary>(StatusCodes.Status201Created)
            .Produces<HoldingSummary>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPatch("/{id:guid}", UpdateHoldingAsync)
            .AddEndpointFilter<ValidationFilter<UpdateHoldingRequest>>()
            .WithName("UpdateHolding")
            .WithSummary("Corrects a mistyped position.")
            .WithDescription("Replaces quantity and price. This is not a purchase, so nothing is averaged.")
            .Produces<HoldingSummary>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        // PATCH here and PUT on the settings sections is deliberate: this changes one field of a larger
        // resource, the settings routes replace the whole of a small one.
        group.MapPatch("/{id:guid}/visibility", SetVisibilityAsync)
            .WithName("SetHoldingVisibility")
            .WithSummary("Shows or hides a position on the dashboard.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{id:guid}", RemoveHoldingAsync)
            .WithName("RemoveHolding")
            .WithSummary("Closes a position.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Outside the group, and deliberately not nested under /api/holdings: it is one resource of its
        // own, not a sub-resource of a position.
        app.MapGet(DashboardPath, GetDashboardAsync)
            .RequireAuthorization()
            .WithTags("Dashboard")
            .WithName("GetDashboard")
            .WithSummary("Prices every visible position and totals the portfolio.")
            .WithDescription("An unpriced position is listed with nulls rather than zeros, and is excluded from the totals and from the weight denominator.")
            .Produces<GetDashboardResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        var settings = app.MapGroup("/api/settings")
            .WithTags("Settings")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        settings.MapGet("/dashboard", GetDashboardSettingsAsync)
            .WithName("GetDashboardSettings")
            .WithSummary("Returns the caller's dashboard refresh interval, defaulting to 60 seconds.")
            .Produces<GetDashboardSettingsResult>(StatusCodes.Status200OK);

        settings.MapPut("/dashboard", SaveDashboardSettingsAsync)
            .AddEndpointFilter<ValidationFilter<SaveDashboardSettingsRequest>>()
            .WithName("SaveDashboardSettings")
            .WithSummary("Saves the caller's dashboard refresh interval.")
            .Produces<GetDashboardSettingsResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        return app;
    }

    /// <summary>Prices the caller's positions.</summary>
    private static async Task<IResult> GetDashboardAsync(
        ClaimsPrincipal principal,
        IQueryHandler<GetDashboardQuery, GetDashboardResult> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        return TypedResults.Ok(await handler.Handle(new GetDashboardQuery(userId), ct));
    }

    /// <summary>Lists the caller's positions.</summary>
    private static async Task<IResult> GetHoldingsAsync(
        ClaimsPrincipal principal,
        IQueryHandler<GetHoldingsQuery, IReadOnlyList<HoldingSummary>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        return TypedResults.Ok(await handler.Handle(new GetHoldingsQuery(userId), ct));
    }

    /// <summary>Records a purchase.</summary>
    private static async Task<IResult> AddHoldingAsync(
        AddHoldingRequest request,
        ClaimsPrincipal principal,
        ICommandHandler<AddHoldingCommand, OneOf<HoldingCreated, HoldingMerged, InvalidInput, UnknownTicker>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(
            new AddHoldingCommand(userId, request.Ticker, request.Quantity, request.Price),
            ct);

        return result.Match(
            // 201 with a Location, because this position did not exist a moment ago.
            created => Results.Created($"{BasePath}/{created.Holding.Id}", created.Holding),

            // 200 and no Location: the position already existed and this purchase changed it.
            merged => Results.Ok(merged.Holding),

            // The handler's own InvalidInput case, not the filter's.
            invalid => invalid.ToValidationProblem(),

            // Shape or existence: the handler asked the provider, and it said no.
            unknownTicker => new InvalidInput(
                    "ticker",
                    $"'{unknownTicker.Ticker}' is not a ticker this application recognises.")
                .ToValidationProblem());
    }

    /// <summary>Corrects a position.</summary>
    private static async Task<IResult> UpdateHoldingAsync(
        Guid id,
        UpdateHoldingRequest request,
        ClaimsPrincipal principal,
        ICommandHandler<UpdateHoldingCommand, OneOf<HoldingSummary, NotFound, InvalidInput>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(
            new UpdateHoldingCommand(userId, id, request.Quantity, request.Price),
            ct);

        return result.Match(
            corrected => Results.Ok(corrected),

            // 404 and never 403: a 403 would confirm to a stranger that this id exists.
            missing => ProblemDetailsExtensions.NotFoundProblem("No such position."),

            invalid => invalid.ToValidationProblem());
    }

    /// <summary>Shows or hides a position on the dashboard.</summary>
    private static async Task<IResult> SetVisibilityAsync(
        Guid id,
        SetHoldingVisibilityRequest request,
        ClaimsPrincipal principal,
        ICommandHandler<SetHoldingVisibilityCommand, OneOf<Success, NotFound>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(new SetHoldingVisibilityCommand(userId, id, request.IsVisible), ct);

        return result.Match(
            hidden => Results.NoContent(),
            missing => ProblemDetailsExtensions.NotFoundProblem("No such position."));
    }

    /// <summary>Closes a position.</summary>
    private static async Task<IResult> RemoveHoldingAsync(
        Guid id,
        ClaimsPrincipal principal,
        ICommandHandler<RemoveHoldingCommand, OneOf<Success, NotFound>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(new RemoveHoldingCommand(userId, id), ct);

        return result.Match(
            closed => Results.NoContent(),
            missing => ProblemDetailsExtensions.NotFoundProblem("No such position."));
    }

    // Reads the caller's dashboard settings, creating the default row on first read.
    private static async Task<IResult> GetDashboardSettingsAsync(
        ClaimsPrincipal principal,
        IQueryHandler<GetDashboardSettingsQuery, GetDashboardSettingsResult> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        return TypedResults.Ok(await handler.Handle(new GetDashboardSettingsQuery(userId), ct));
    }

    // Saves the caller's dashboard settings.
    private static async Task<IResult> SaveDashboardSettingsAsync(
        SaveDashboardSettingsRequest request,
        ClaimsPrincipal principal,
        ICommandHandler<SaveDashboardSettingsCommand, OneOf<GetDashboardSettingsResult, InvalidInput>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(
            new SaveDashboardSettingsCommand(userId, request.RefreshIntervalSeconds),
            ct);

        return result.Match(
            saved => Results.Ok(saved),

            // Reachable only if the validator and RefreshInterval.Create disagree about the allowed range.
            invalid => invalid.ToValidationProblem());
    }

    /// <summary>Reads the subject claim. Totality over a string?, not a security control.</summary>
    private static bool TryReadUserId(ClaimsPrincipal principal, out Guid userId, out IResult rejection)
    {
        // OnTokenValidated already rejects a subject-less token; this only gives null a branch.
        if (Guid.TryParse(principal.FindFirstValue(SubjectClaimType), out userId))
        {
            rejection = TypedResults.Empty;
            return true;
        }

        rejection = ProblemDetailsExtensions.UnauthorizedProblem("The access token carries no usable subject.");
        return false;
    }
}
