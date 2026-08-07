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

public static partial class MarketDataEndpoints
{
    private const string HealthPath = "/api/marketdata/health";

    private const string SearchPath = "/api/marketdata/search";

    private const string NudgePath = "/api/dev/nudge";

    private const string SubjectClaimType = "sub";

    public static IServiceCollection AddMarketDataApi(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<NudgeRequestValidator>();

        return services;
    }

    public static IEndpointRouteBuilder MapMarketDataEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var services = app.ServiceProvider;
        var provider = services.GetRequiredService<IQuoteProvider>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(MarketDataEndpoints));

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

        app.MapGet(SearchPath, SearchTickersAsync)
            .RequireAuthorization()
            .WithTags("MarketData")
            .WithName("SearchTickers")
            .WithSummary("Suggests symbols matching what has been typed so far.")
            .WithDescription("A query too short to mean anything, and a provider that cannot answer, both come back as an empty list with 200 — picking from the list is a convenience, and typing a symbol always works.")
            .Produces<IReadOnlyList<SearchTickersResult>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

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

    private static async Task<IResult> SearchTickersAsync(
        string? q,
        IQueryHandler<SearchTickersQuery, IReadOnlyList<SearchTickersResult>> handler,
        CancellationToken ct) =>
        TypedResults.Ok(await handler.Handle(new SearchTickersQuery(q), ct));

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

            rejected => new InvalidInput("apiKey", "The provider rejected this key.").ToValidationProblem(),

            unanswerable => ProblemDetailsExtensions.ServiceUnavailableProblem(
                "The provider could not be reached to verify this key. Nothing was changed."),

            disabled => ProblemDetailsExtensions.NotFoundProblem("This feature is not enabled."));
    }

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

public sealed record MarketDataHealthResult(string Provider);
