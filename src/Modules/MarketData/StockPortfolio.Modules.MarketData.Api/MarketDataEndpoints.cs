using FluentValidation;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using StockPortfolio.Modules.MarketData.Api.Requests;
using StockPortfolio.Modules.MarketData.Api.Validators;
using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Application.Tickers.Queries.SearchTickers;
using StockPortfolio.Shared.Api;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.MarketData.Api;

/// <summary>MarketData's entire inbound HTTP surface: health, ticker search, and a dev-only price nudge.</summary>
public static partial class MarketDataEndpoints
{
    /// <summary>Anonymous, and shipped in every environment: the SPA's health panel reads it.</summary>
    private const string HealthPath = "/api/marketdata/health";

    /// <summary>Ticker suggestions for the add-position form. Under /api/marketdata/, beside the health route.</summary>
    private const string SearchPath = "/api/marketdata/search";

    /// <summary>Development only, and only while a nudgeable provider is registered.</summary>
    private const string NudgePath = "/api/dev/nudge";

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

        return app;
    }

    /// <summary>Suggests symbols. A bare array, matching GET /api/holdings.</summary>
    private static async Task<IResult> SearchTickersAsync(
        string? q,
        IQueryHandler<SearchTickersQuery, IReadOnlyList<SearchTickersResult>> handler,
        CancellationToken ct) =>
        TypedResults.Ok(await handler.Handle(new SearchTickersQuery(q), ct));

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
