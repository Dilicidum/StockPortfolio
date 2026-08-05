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
using StockPortfolio.Shared.Api;

namespace StockPortfolio.Modules.MarketData.Api;

/// <summary>MarketData's entire inbound HTTP surface: the health route, and a dev-only price nudge.</summary>
public static partial class MarketDataEndpoints
{
    /// <summary>Anonymous, and shipped in every environment: the SPA's health panel reads it.</summary>
    private const string HealthPath = "/api/marketdata/health";

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
