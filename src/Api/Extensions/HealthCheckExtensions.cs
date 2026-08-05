using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using StockPortfolio.Api.HealthChecks;

namespace StockPortfolio.Api.Extensions;

/// <summary>The two health endpoints and the checks behind them.</summary>
internal static class HealthCheckExtensions
{
    /// <summary>The path ACA's liveness probe calls, which must never touch a dependency.</summary>
    public const string LivenessPath = "/health/live";

    /// <summary>The path ACA's readiness probe calls, which runs every dependency check.</summary>
    public const string ReadinessPath = "/health/ready";

    private const string PostgresCheckName = "postgres";
    private const string RedisCheckName = "redis";

    /// <summary>Registers the two readiness checks. The multiplexer they use is AddStockPortfolioRedis'.</summary>
    public static IServiceCollection AddStockPortfolioHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(PostgresCheckName)
            .AddCheck<RedisHealthCheck>(RedisCheckName);

        return services;
    }

    /// <summary>Maps the anonymous liveness and readiness routes.</summary>
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
