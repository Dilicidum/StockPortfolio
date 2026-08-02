using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using StackExchange.Redis;

using StockPortfolio.Api.HealthChecks;

namespace StockPortfolio.Api.Extensions;

/// <summary>The two health endpoints and the checks behind them.</summary>
public static class HealthCheckExtensions
{
    /// <summary>Liveness.</summary>
    public const string LivenessPath = "/health/live";

    /// <summary>Readiness.</summary>
    public const string ReadinessPath = "/health/ready";

    /// <summary>The ConnectionStrings key holding the Redis endpoint, e.g.</summary>
    public const string RedisConnectionStringName = "Redis";

    private const string PostgresCheckName = "postgres";
    private const string RedisCheckName = "redis";

    /// <summary>Registers the shared Redis multiplexer and the two readiness checks.</summary>
    public static IServiceCollection AddStockPortfolioHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var redisConnectionString = configuration.GetConnectionString(RedisConnectionStringName);

        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{RedisConnectionStringName}' is not configured. Set "
                + $"ConnectionStrings__{RedisConnectionStringName} (compose and Bicep both do). Phase 3 "
                + "puts price windows, alert cooldowns and SSE tickets here, so the topology is wired and "
                + "probed from Phase 1 rather than discovered to be unreachable two phases later.");
        }

        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);

        // AbortOnConnectFail=false: a Redis blip must not kill startup.
        redisOptions.AbortOnConnectFail = false;

        // Resolved lazily: the singleton factory does not run until something asks for it, which in Phase 1.
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(PostgresCheckName)
            .AddCheck<RedisHealthCheck>(RedisCheckName);

        return services;
    }

    /// <summary>Maps LivenessPath and ReadinessPath.</summary>
    public static IEndpointRouteBuilder MapStockPortfolioHealthChecks(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

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
