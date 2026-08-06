using System.Globalization;
using System.Security.Claims;

using FluentValidation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

using OneOf;

using StockPortfolio.Modules.Alerts.Api.Requests;
using StockPortfolio.Modules.Alerts.Api.Validators;
using StockPortfolio.Modules.Alerts.Application.History.Queries.GetFiredAlerts;
using StockPortfolio.Modules.Alerts.Application.Settings.Commands.SaveAlertSetting;
using StockPortfolio.Modules.Alerts.Application.Settings.Queries.GetAlertSettings;
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

    /// <summary>Registers the module's presentation-layer services: the request validators.</summary>
    public static IServiceCollection AddAlertsApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<SaveAlertSettingRequestValidator>();

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

        group.MapPut("/settings", SaveAlertSettingAsync)
            .AddEndpointFilter<ValidationFilter<SaveAlertSettingRequest>>()
            .WithName("SaveAlertSetting")
            .WithSummary("Sets or changes the threshold on one position.")
            .WithDescription("One threshold per position: saving twice for the same ticker replaces the first rather than adding a second.")
            .Produces<SaveAlertSettingResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType);

        return app;
    }

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

        return result.Match<IResult>(
            saved => TypedResults.Ok(saved),

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
