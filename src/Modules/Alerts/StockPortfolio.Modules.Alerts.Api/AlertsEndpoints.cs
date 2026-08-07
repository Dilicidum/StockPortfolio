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

public static class AlertsEndpoints
{
    private const string BasePath = "/api/alerts";

    private const string SubjectClaimType = "sub";

    public const string HubPath = "/api/alerts/stream";

    public static IServiceCollection AddAlertsApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<SaveAlertSettingRequestValidator>();

        services.AddScoped<IAlertPublisher, SignalRAlertPublisher>();

        services.AddSingleton<IUserIdProvider, SubjectClaimUserIdProvider>();

        return services;
    }

    public static IEndpointRouteBuilder MapAlertsEndpoints(this IEndpointRouteBuilder app)
    {
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

        app.MapHub<AlertsHub>(HubPath);

        return app;
    }

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
            simulated => Results.Accepted((string?)null),

            nothing => ProblemDetailsExtensions.ConflictProblem(Describe(nothing)));
    }

    private static string Describe(NoPositionToSimulate nothing) => nothing.Ticker is null
        ? "You have no enabled threshold to simulate. Set one on a position first."
        : $"You have no enabled threshold on {nothing.Ticker}, so there is nothing to simulate for it.";

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

        return TypedResults.Ok(
            await handler.Handle(new GetFiredAlertsQuery(userId, limit ?? int.MaxValue), ct));
    }

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

            notHeld => ProblemDetailsExtensions.ConflictProblem(
                $"You hold no position in {notHeld.Ticker}, so there is nothing to set a threshold on."),

            tooLong => ProblemDetailsExtensions.ConflictProblem(Describe(tooLong)),

            invalid => invalid.ToValidationProblem());
    }

    private static string Describe(WindowExceedsRetention tooLong)
    {
        var requested = tooLong.RequestedMinutes.ToString(CultureInfo.InvariantCulture);
        var maximum = tooLong.MaximumMinutes.ToString(CultureInfo.InvariantCulture);

        return $"A {requested}-minute window is longer than the {maximum}-minute maximum. "
            + "A move measured over days is a trend, not a sharp move.";
    }

    private static bool TryReadUserId(ClaimsPrincipal principal, out Guid userId, out IResult rejection)
    {
        if (Guid.TryParse(principal.FindFirstValue(SubjectClaimType), out userId))
        {
            rejection = TypedResults.Empty;
            return true;
        }

        rejection = ProblemDetailsExtensions.UnauthorizedProblem("The access token carries no usable subject.");
        return false;
    }
}
