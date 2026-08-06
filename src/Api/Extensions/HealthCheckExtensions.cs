using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using StackExchange.Redis;

namespace StockPortfolio.Api.Extensions;

/// <summary>The two health endpoints, and the one check the host owns.</summary>
/// <remarks>
/// The four Postgres checks are NOT here. Each module registers its own in its Add&lt;M&gt;Module, because
/// its DbContext is internal and the host cannot name the type. Readiness therefore covers all four
/// logins rather than the one it used to.
/// </remarks>
internal static class HealthCheckExtensions
{
    /// <summary>The path ACA's liveness probe calls, which must never touch a dependency.</summary>
    public const string LivenessPath = "/health/live";

    /// <summary>The path ACA's readiness probe calls, which runs every dependency check.</summary>
    public const string ReadinessPath = "/health/ready";

    /// <summary>The readiness entry name for Redis. The Postgres entries are postgres-&lt;module&gt;.</summary>
    public const string RedisCheckName = "redis";

    /// <summary>Registers the Redis readiness check against the multiplexer the application itself holds.</summary>
    public static IServiceCollection AddStockPortfolioHealthChecks(this IServiceCollection services)
    {
        // The factory overload, deliberately: it resolves the very IConnectionMultiplexer that
        // AddStockPortfolioRedis registered, so a green answer means the connection this process is
        // using is healthy — not that some connection could be opened. The connection-string overload
        // would answer the weaker question. The package also pings the server, not only the database,
        // and understands cluster state, which the hand-written check this replaces did not.
        services.AddHealthChecks()
            .AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(), name: RedisCheckName);

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
