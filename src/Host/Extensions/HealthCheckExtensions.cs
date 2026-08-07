using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using StackExchange.Redis;

namespace StockPortfolio.Host.Extensions;

/// <summary>The three health endpoints and the one check the host owns; each module registers its own Postgres check, because its DbContext is internal to it.</summary>
internal static class HealthCheckExtensions
{
    public const string LivenessPath = "/health/live";

    public const string ReadinessPath = "/health/ready";

    public const string DetailPath = "/api/health/detail";

    public const string RedisCheckName = "redis";

    /// <summary>Runs on the readiness probe, so an Unhealthy answer here withdraws the replica.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Appears in the authenticated detail report, which answers 200 whatever it finds.</summary>
    public const string DetailTag = "detail";

    public static IServiceCollection AddStockPortfolioHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                name: RedisCheckName,
                failureStatus: HealthStatus.Degraded,
                tags: [ReadyTag, DetailTag]);

        return services;
    }

    public static IEndpointRouteBuilder MapStockPortfolioHealthChecks(this IEndpointRouteBuilder app)
    {
        // Predicate = _ => false selects zero checks: liveness must never touch a dependency.
        app.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous()
            .WithName("Liveness");

        app.MapHealthChecks(ReadinessPath, new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(ReadyTag),
                ResponseWriter = WriteReportAsync,
            })
            .AllowAnonymous()
            .WithName("Readiness");

        app.MapHealthChecks(DetailPath, new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(DetailTag),
                ResponseWriter = WriteReportAsync,
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                    [HealthStatus.Unhealthy] = StatusCodes.Status200OK,
                },
            })
            .RequireAuthorization()
            .WithName("GetHealthDetail")
            // MapHealthChecks hands back an IEndpointConventionBuilder, so .Produces does not chain here.
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(void), ["application/json"]))
            .WithMetadata(new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), ["application/problem+json"]));

        return app;
    }

    /// <summary>Shared by readiness and the detail route, so the deploy smoke step and the browser read one shape.</summary>
    private static Task WriteReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var body = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            components = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
                data = entry.Value.Data,
            }),
        };

        return context.Response.WriteAsJsonAsync(body, context.RequestAborted);
    }
}
