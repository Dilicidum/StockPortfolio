using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using StackExchange.Redis;

namespace StockPortfolio.Host.Extensions;

/// <summary>The two health endpoints and the Redis check; each module registers its own Postgres check, because its DbContext is internal to it.</summary>
internal static class HealthCheckExtensions
{
    public const string LivenessPath = "/health/live";

    public const string ReadinessPath = "/health/ready";

    public const string RedisCheckName = "redis";

    public static IServiceCollection AddStockPortfolioHealthChecks(this IServiceCollection services)
    {
        // The factory overload deliberately: it checks the multiplexer this process is actually using, not one it could open.
        services.AddHealthChecks()
            .AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: RedisCheckName);

        return services;
    }

    public static IEndpointRouteBuilder MapStockPortfolioHealthChecks(this IEndpointRouteBuilder app)
    {
        // Predicate = _ => false selects zero checks: liveness must never touch a dependency.
        app.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous()
            .WithName("Liveness");

        app.MapHealthChecks(ReadinessPath)
            .AllowAnonymous()
            .WithName("Readiness");

        return app;
    }
}
