using System.Globalization;
using System.Security.Claims;

using FluentValidation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.Alerts.Api.Requests;
using StockPortfolio.Modules.Alerts.Api.Streaming;
using StockPortfolio.Modules.Alerts.Api.Validators;
using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Application.History.Queries.GetFiredAlerts;
using StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;
using StockPortfolio.Modules.Alerts.Application.Settings.Queries.GetAlertSettings;
using StockPortfolio.Modules.Alerts.Application.Simulation.Commands.SimulateAlert;
using StockPortfolio.Shared.Api;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Alerts.Api;

/// <summary>The Alerts module's inbound HTTP surface, under /api/alerts, and the one DI seam.</summary>
public static class AlertsEndpoints
{
    /// <summary>Where a threshold and its history live.</summary>
    private const string BasePath = "/api/alerts";

    /// <summary>The claim carrying the user id.</summary>
    private const string SubjectClaimType = "sub";

    /// <summary>Where the hub lives. nginx matches this prefix and turns buffering off for it.</summary>
    public const string HubPath = "/api/alerts/stream";

    /// <summary>Registers the module's presentation-layer services: validators, and the alert fan-out.</summary>
    public static IServiceCollection AddAlertsApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<SaveAlertSettingRequestValidator>();

        // The publisher lives in .Api rather than .Infrastructure because IHubContext is ASP.NET Core
        // and that layer may not reference it. AddAlertsModule registers no IAlertPublisher at all now.
        services.AddScoped<IAlertPublisher, SignalRAlertPublisher>();

        // Without this, Clients.User matches on a claim these tokens do not carry, and every alert is
        // delivered to nobody with no error anywhere.
        services.AddSingleton<IUserIdProvider, SubjectClaimUserIdProvider>();

        return services;
    }

    /// <summary>Maps the alert routes onto /api/alerts.</summary>
    public static IEndpointRouteBuilder MapAlertsEndpoints(this IEndpointRouteBuilder app)
    {
        // Every route needs a bearer token and every route can 500, so both are declared once here.
        var group = app.MapGroup(BasePath)
            .RequireAuthorization()
            .WithTags("Alerts")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        group.MapGet("/", GetFiredAlertsAsync)
            .WithName("GetAlerts")
            .WithSummary("Lists the caller's recent alerts, newest first.")
            .WithDescription("The panel and the notifications screen read the same list; a limit outside the server's range is clamped rather than refused.")
            .Produces<IReadOnlyList<GetFiredAlertsResult>>(StatusCodes.Status200OK);

        group.MapGet("/settings", GetAlertSettingsAsync)
            .WithName("GetAlertSettings")
            .WithSummary("Lists every threshold the caller has set.")
            .WithDescription("A user with no thresholds gets an empty list, never a 404 — the portfolio page reads this on every mount.")
            .Produces<IReadOnlyList<GetAlertSettingsResult>>(StatusCodes.Status200OK);

        group.MapPost("/simulate", SimulateAlertAsync)
            .AddEndpointFilter<ValidationFilter<SimulateAlertRequest>>()
            .WithName("SimulateAlert")
            .WithSummary("Fires one alert on demand, down the real path.")
            .WithDescription("Saved and then published exactly as an evaluated alert is, so what arrives proves the mechanism rather than the button. An omitted ticker lets the server pick one of the caller's thresholds.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        group.MapPut("/settings", SaveAlertSettingAsync)
            .AddEndpointFilter<ValidationFilter<SaveAlertSettingRequest>>()
            .WithName("SaveAlertSetting")
            .WithSummary("Sets or changes the threshold on one position.")
            .WithDescription("One threshold per position: saving twice for the same ticker replaces the first rather than adding a second.")
            .Produces<SaveAlertSettingResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        // Outside the group: a hub is not a minimal-API endpoint, so the group's filters, its
        // Produces metadata and its RequireAuthorization do not apply. The hub carries its own
        // [Authorize], and its transport is negotiated rather than declared in OpenAPI.
        app.MapHub<AlertsHub>(HubPath);

        return app;
    }

    /// <summary>Fires one alert on demand.</summary>
    private static async Task<IResult> SimulateAlertAsync(
        SimulateAlertRequest request,
        ClaimsPrincipal principal,
        ICommandHandler<SimulateAlertCommand, OneOf<Success, NoPositionToSimulate>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(new SimulateAlertCommand(userId, request.Ticker), ct);

        return result.Match(
            // 202, not 200: the row is written here but the arrival happens on a connection this
            // request knows nothing about, and there is no body worth inventing.
            simulated => Results.Accepted((string?)null),

            nothing => ProblemDetailsExtensions.ConflictProblem(Describe(nothing)));
    }

    /// <summary>Says which of the two ways there was nothing to simulate, because the fixes differ.</summary>
    private static string Describe(NoPositionToSimulate nothing) => nothing.Ticker is null
        ? "You have no enabled threshold to simulate. Set one on a position first."
        : $"You have no enabled threshold on {nothing.Ticker}, so there is nothing to simulate for it.";

    /// <summary>Lists the caller's recent alerts.</summary>
    private static async Task<IResult> GetFiredAlertsAsync(
        ClaimsPrincipal principal,
        IQueryHandler<GetFiredAlertsQuery, IReadOnlyList<GetFiredAlertsResult>> handler,
        CancellationToken ct,
        int? limit = null)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        // An absent ?limit= asks for as many as the server is willing to give, which is the same
        // number the handler would clamp anything larger down to.
        return TypedResults.Ok(
            await handler.Handle(new GetFiredAlertsQuery(userId, limit ?? int.MaxValue), ct));
    }

    /// <summary>Lists the caller's thresholds.</summary>
    private static async Task<IResult> GetAlertSettingsAsync(
        ClaimsPrincipal principal,
        IQueryHandler<GetAlertSettingsQuery, IReadOnlyList<GetAlertSettingsResult>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        return TypedResults.Ok(await handler.Handle(new GetAlertSettingsQuery(userId), ct));
    }

    /// <summary>Sets or changes one threshold.</summary>
    private static async Task<IResult> SaveAlertSettingAsync(
        SaveAlertSettingRequest request,
        ClaimsPrincipal principal,
        ICommandHandler<
            SaveAlertSettingCommand,
            OneOf<SaveAlertSettingResult, TickerNotHeld, WindowExceedsRetention, InvalidInput>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(
            new SaveAlertSettingCommand(
                userId,
                request.Ticker,
                request.ThresholdPercent,
                request.WindowMinutes,
                request.Enabled),
            ct);

        return result.Match(
            saved => Results.Ok(saved),

            // 409 rather than 400: the body is well formed and the request is refused by the state of
            // the portfolio, which the caller can fix by buying the position.
            notHeld => ProblemDetailsExtensions.ConflictProblem(
                $"You hold no position in {notHeld.Ticker}, so there is nothing to set a threshold on."),

            // Both numbers, because "too long" without the cap is a message nobody can act on.
            tooLong => ProblemDetailsExtensions.ConflictProblem(Describe(tooLong)),

            invalid => invalid.ToValidationProblem());
    }

    /// <summary>Names the window asked for and the cap it broke, so the caller can pick a legal one.</summary>
    private static string Describe(WindowExceedsRetention tooLong)
    {
        var requested = tooLong.RequestedMinutes.ToString(CultureInfo.InvariantCulture);
        var maximum = tooLong.MaximumMinutes.ToString(CultureInfo.InvariantCulture);

        return $"A {requested}-minute window is longer than the {maximum}-minute maximum. "
            + "A move measured over days is a trend, not a sharp move.";
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
