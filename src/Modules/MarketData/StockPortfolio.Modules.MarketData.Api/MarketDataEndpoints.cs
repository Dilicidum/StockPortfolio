using System.Security.Claims;

using FluentValidation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OneOf;
using OneOf.Types;

using StockPortfolio.Modules.MarketData.Api.Requests;
using StockPortfolio.Modules.MarketData.Api.Validators;
using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Application.Keys.Commands.RemoveApiKey;
using StockPortfolio.Modules.MarketData.Application.Keys.Commands.SaveApiKey;
using StockPortfolio.Modules.MarketData.Application.Keys.Queries.GetApiKeyStatus;
using StockPortfolio.Modules.MarketData.Application.Tickers.Queries.SearchTickers;
using StockPortfolio.Shared.Api;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.MarketData.Api;

/// <summary>MarketData's entire inbound HTTP surface: health, ticker search, the BYOK settings trio, and a dev-only price nudge.</summary>
public static partial class MarketDataEndpoints
{
    /// <summary>Anonymous, and shipped in every environment: the SPA's health panel reads it.</summary>
    private const string HealthPath = "/api/marketdata/health";

    /// <summary>Ticker suggestions for the add-position form. Under /api/marketdata/, beside the health route.</summary>
    private const string SearchPath = "/api/marketdata/search";

    /// <summary>Development only, and only while a nudgeable provider is registered.</summary>
    private const string NudgePath = "/api/dev/nudge";

    /// <summary>The claim carrying the user id.</summary>
    private const string SubjectClaimType = "sub";

    /// <summary>Registers the module's presentation-layer services: the request validators.</summary>
    public static IServiceCollection AddMarketDataApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<NudgeRequestValidator>();

        return services;
    }

    /// <summary>Maps the health route, announces the active provider, and maps the nudge if it applies.</summary>
    public static IEndpointRouteBuilder MapMarketDataEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var services = app.ServiceProvider;
        var provider = services.GetRequiredService<IQuoteProvider>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MarketDataEndpoints));

        // Emitted here because Map runs exactly once, eagerly, after Build() and before RunAsync(). A DI
        // factory lambda would fire on first RESOLUTION instead, so with no dashboard request the line
        // announcing a fake-priced deployment would never appear at all.
        var nudge = services.GetService<IQuoteNudge>();

        if (nudge is null)
        {
            LogLiveProvider(logger, provider.Name);
        }
        else
        {
            LogGeneratedPrices(logger, provider.Name);
        }

        app.MapGet(HealthPath, () => TypedResults.Ok(new MarketDataHealthResult(provider.Name)))
            .AllowAnonymous()
            .WithTags("MarketData")
            .WithName("GetMarketDataHealth")
            .WithSummary("Names the quote provider this instance is serving prices from.")
            .WithDescription("The single string the startup log and the SPA's health panel both read, so the two cannot drift.")
            .Produces<MarketDataHealthResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // No ValidationFilter and no 400: an empty, short or nonsense q is an empty list, not an error.
        // The form behind this is already behind sign-in, so the route is too.
        app.MapGet(SearchPath, SearchTickersAsync)
            .RequireAuthorization()
            .WithTags("MarketData")
            .WithName("SearchTickers")
            .WithSummary("Suggests symbols matching what has been typed so far.")
            .WithDescription("A query too short to mean anything, and a provider that cannot answer, both come back as an empty list with 200 — picking from the list is a convenience, and typing a symbol always works.")
            .Produces<IReadOnlyList<SearchTickersResult>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // Gated on the PROVIDER as well as the environment. In Azure the environment is Production so the
        // route does not exist at all — 404, not 401 — and with a real key there is no IQuoteNudge to map
        // even if someone deletes the environment check. RequireAuthorization is deliberately not the gate:
        // a price-manipulation endpoint any signed-in user can reach in production is still one.
        if (nudge is not null && services.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            app.MapPost(NudgePath, (NudgeRequest request, IQuoteNudge target) =>
                {
                    target.Nudge(request.Ticker, request.Percent, TimeSpan.FromSeconds(request.TtlSeconds));

                    return TypedResults.NoContent();
                })
                .AddEndpointFilter<ValidationFilter<NudgeRequest>>()
                .WithTags("Dev")
                .WithName("NudgePrice")
                .WithSummary("Shifts one generated price by a percentage, for a while.")
                .Produces(StatusCodes.Status204NoContent)
                .ProducesValidationProblem()
                .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
                .ProducesProblem(StatusCodes.Status500InternalServerError);
        }

        // No shared group existed here before this route: the health and search routes map straight off
        // app, with no RequireAuthorization, no shared 401 and no shared 500. Two other modules already
        // map a group at this path; this makes three.
        var settings = app.MapGroup("/api/settings")
            .WithTags("Settings")
            .RequireAuthorization()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        settings.MapGet("/api-key", GetApiKeyStatusAsync)
            .WithName("GetApiKeyStatus")
            .WithSummary("Reports whether the caller has their own provider key on file.")
            .WithDescription("The key itself is never in the response, not even masked beyond the last four.")
            .Produces<GetApiKeyStatusResult>(StatusCodes.Status200OK);

        settings.MapPost("/api-key", SaveApiKeyAsync)
            .AddEndpointFilter<ValidationFilter<SaveApiKeyRequest>>()
            .WithName("SaveApiKey")
            .WithSummary("Validates a key against the live provider, then stores it encrypted.")
            .WithDescription("404 when BYOK is switched off — a disabled feature should not advertise itself.")
            .Produces<GetApiKeyStatusResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        settings.MapDelete("/api-key", RemoveApiKeyAsync)
            .WithName("RemoveApiKey")
            .WithSummary("Forgets the caller's own provider key.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    /// <summary>Suggests symbols. A bare array, matching GET /api/holdings.</summary>
    private static async Task<IResult> SearchTickersAsync(
        string? q,
        IQueryHandler<SearchTickersQuery, IReadOnlyList<SearchTickersResult>> handler,
        CancellationToken ct) =>
        TypedResults.Ok(await handler.Handle(new SearchTickersQuery(q), ct));

    /// <summary>Reads the caller's key status.</summary>
    private static async Task<IResult> GetApiKeyStatusAsync(
        ClaimsPrincipal principal,
        IQueryHandler<GetApiKeyStatusQuery, GetApiKeyStatusResult> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        return TypedResults.Ok(await handler.Handle(new GetApiKeyStatusQuery(userId), ct));
    }

    /// <summary>Validates then stores the caller's own key. Never echoes it back, on any path.</summary>
    private static async Task<IResult> SaveApiKeyAsync(
        SaveApiKeyRequest request,
        ClaimsPrincipal principal,
        ICommandHandler<
            SaveApiKeyCommand,
            OneOf<SaveApiKeyResult, ProviderRejectedTheKey, ProviderCouldNotAnswer, ByokDisabled>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(new SaveApiKeyCommand(userId, request.ApiKey), ct);

        return result.Match(
            saved => Results.Ok(new GetApiKeyStatusResult(true, saved.LastFour, false)),

            // The handler's own failure case, not the filter's — the request was well-shaped, the
            // provider just said no to this exact key.
            rejected => new InvalidInput("apiKey", "The provider rejected this key.").ToValidationProblem(),

            // Distinct from "rejected": the provider could not be asked at all, so this must never be
            // read as "your key is bad" — that would be the same class of mistake as the c: 0 trap.
            unanswerable => ProblemDetailsExtensions.ServiceUnavailableProblem(
                "The provider could not be reached to verify this key. Nothing was changed."),

            // 404, not 403: a switched-off feature should not confirm its own existence either.
            disabled => ProblemDetailsExtensions.NotFoundProblem("This feature is not enabled."));
    }

    /// <summary>Forgets the caller's own key.</summary>
    private static async Task<IResult> RemoveApiKeyAsync(
        ClaimsPrincipal principal,
        ICommandHandler<RemoveApiKeyCommand, OneOf<Success, NotFound>> handler,
        CancellationToken ct)
    {
        if (!TryReadUserId(principal, out var userId, out var rejection))
        {
            return rejection;
        }

        var result = await handler.Handle(new RemoveApiKeyCommand(userId), ct);

        return result.Match(
            removed => Results.NoContent(),
            missing => ProblemDetailsExtensions.NotFoundProblem("No provider key is configured."));
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

    [LoggerMessage(
        EventId = 5200,
        Level = LogLevel.Information,
        Message = "Serving live prices from the {Provider} quote provider")]
    private static partial void LogLiveProvider(ILogger logger, string provider);

    [LoggerMessage(
        EventId = 5201,
        Level = LogLevel.Warning,
        Message = "No Finnhub__ApiKey configured; serving generated prices from the {Provider} quote provider")]
    private static partial void LogGeneratedPrices(ILogger logger, string provider);
}

/// <summary>What GET /api/marketdata/health returns.</summary>
public sealed record MarketDataHealthResult(string Provider);
