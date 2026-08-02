using Microsoft.AspNetCore.Diagnostics.HealthChecks;

using StackExchange.Redis;

using StockPortfolio.Api.HealthChecks;

namespace StockPortfolio.Api.Extensions;

/// <summary>
/// The two health endpoints and the checks behind them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Liveness checks nothing.</b> Container Apps restarts a container whose liveness probe fails, so a
/// liveness probe that touches Postgres turns a database blip into a restart loop — a degraded app
/// becomes a down one. <c>/health/live</c> answers 200 whenever the process can serve a request, which
/// is the only question a restart can answer.
/// </para>
/// <para>
/// <b>Readiness checks Postgres and Redis.</b> Failing readiness takes the replica out of rotation and
/// puts it back when the dependency returns; nothing is killed.
/// </para>
/// <para>
/// The split is inert unless the probes are declared in Bicep: with ingress enabled, ACA injects
/// default <i>TCP</i> probes and never calls either path. See <c>infra/modules/containerapp-api.bicep</c>.
/// </para>
/// </remarks>
public static class HealthCheckExtensions
{
    /// <summary>Liveness. Checks nothing — see the remarks on <see cref="HealthCheckExtensions"/>.</summary>
    public const string LivenessPath = "/health/live";

    /// <summary>Readiness. Checks Postgres and Redis.</summary>
    public const string ReadinessPath = "/health/ready";

    /// <summary>The <c>ConnectionStrings</c> key holding the Redis endpoint, e.g. <c>redis:6379</c>.</summary>
    public const string RedisConnectionStringName = "Redis";

    private const string PostgresCheckName = "postgres";
    private const string RedisCheckName = "redis";

    /// <summary>
    /// Registers the shared Redis multiplexer and the two readiness checks.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Configuration carrying <c>ConnectionStrings:Redis</c>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="InvalidOperationException">The Redis connection string is missing.</exception>
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

        // Without this, ConnectionMultiplexer.Connect throws when Redis is briefly unavailable and takes
        // the whole host down at startup - for a dependency that has no business consumer until Phase 3.
        // False makes the multiplexer reconnect in the background; the readiness check reports the gap.
        redisOptions.AbortOnConnectFail = false;

        // Resolved lazily: the singleton factory does not run until something asks for it, which in
        // Phase 1 is the first readiness probe. Startup therefore never blocks on Redis.
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));

        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>(PostgresCheckName)
            .AddCheck<RedisHealthCheck>(RedisCheckName);

        return services;
    }

    /// <summary>
    /// Maps <see cref="LivenessPath"/> and <see cref="ReadinessPath"/>.
    /// </summary>
    /// <param name="app">The route builder to map onto.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IEndpointRouteBuilder MapStockPortfolioHealthChecks(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Predicate = _ => false selects zero checks, so the endpoint answers 200 as long as the process
        // is answering at all. That is the whole contract.
        app.MapHealthChecks(LivenessPath, new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous()
            .WithName("Liveness");

        app.MapHealthChecks(ReadinessPath)
            .AllowAnonymous()
            .WithName("Readiness");

        return app;
    }
}
