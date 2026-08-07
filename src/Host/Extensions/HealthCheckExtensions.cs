using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using StackExchange.Redis;

using StockPortfolio.Host.Health;

namespace StockPortfolio.Host.Extensions;

/// <summary>The four health endpoints and the two checks the host owns; each module registers its own Postgres check, because its DbContext is internal to it.</summary>
internal static class HealthCheckExtensions
{
    public const string LivenessPath = "/health/live";

    public const string ReadinessPath = "/health/ready";

    public const string StartupPath = "/health/startup";

    public const string DetailPath = "/api/health/detail";

    public const string RedisCheckName = "redis";

    public const string MigrationsCheckName = "migrations";

    /// <summary>Runs on the readiness probe, so an Unhealthy answer here withdraws the replica.</summary>
    public const string ReadyTag = "ready";

    /// <summary>Runs on the startup probe only: it is a database round trip and must never reach liveness.</summary>
    public const string StartupTag = "startup";

    /// <summary>Appears in the authenticated detail report, which answers 200 whatever it finds.</summary>
    public const string DetailTag = "detail";

    public static IServiceCollection AddStockPortfolioHealthChecks(this IServiceCollection services)
    {
        var contextTypes = DbContextTypesIn(services);

        if (contextTypes.Count == 0)
        {
            throw new InvalidOperationException(
                "No DbContext is registered, so the pending-migrations check would report healthy by "
                + "asking nothing. Call AddStockPortfolioHealthChecks after every Add<M>Module.");
        }

        services.AddHealthChecks()

            // Degraded, not the default Unhealthy: a cache outage answering 503 on readiness pulls every replica out of rotation, and the framework already maps Degraded to 200.
            .AddRedis(
                sp => sp.GetRequiredService<IConnectionMultiplexer>(),
                name: RedisCheckName,
                failureStatus: HealthStatus.Degraded,
                tags: [ReadyTag, DetailTag])

            .Add(new HealthCheckRegistration(
                MigrationsCheckName,
                sp => new PendingMigrationsHealthCheck(sp, contextTypes),
                HealthStatus.Unhealthy,
                tags: [StartupTag]));

        return services;
    }

    public static IEndpointRouteBuilder MapStockPortfolioHealthChecks(this IEndpointRouteBuilder app)
    {
        // Predicate = _ => false selects zero checks: liveness must never touch a dependency.
        app.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous()
            .WithName("Liveness");

        // Unhealthy keeps its 503 here, which is what takes a genuinely broken replica out of rotation.
        app.MapHealthChecks(ReadinessPath, new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(ReadyTag),
                ResponseWriter = WriteReportAsync,
            })
            .AllowAnonymous()
            .WithName("Readiness");

        // The same writer again, so a test can assert this probe runs the migrations check and nothing else.
        app.MapHealthChecks(StartupPath, new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains(StartupTag),
                ResponseWriter = WriteReportAsync,
            })
            .AllowAnonymous()
            .WithName("Startup");

        // Every status maps to 200: a route whose job is to report that Postgres is down cannot use that failure as its own answer.
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
            .WithName("GetHealthDetail");

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

    private static IReadOnlyList<Type> DbContextTypesIn(IServiceCollection services) =>
    [
        .. services
            .Where(descriptor => descriptor.ServiceType.IsSubclassOf(typeof(DbContext)))
            .Select(descriptor => descriptor.ServiceType)
            .Distinct()
            .OrderBy(type => type.Name, StringComparer.Ordinal),
    ];
}
