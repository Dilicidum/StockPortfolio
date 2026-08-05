using StackExchange.Redis;

namespace StockPortfolio.Api.Extensions;

/// <summary>The one place the Redis multiplexer is built, so "who owns it" is answerable in one file.</summary>
internal static class RedisExtensions
{
    /// <summary>The ConnectionStrings key holding the Redis endpoint.</summary>
    public const string RedisConnectionStringName = "Redis";

    /// <summary>Registers the shared multiplexer every Redis consumer injects.</summary>
    public static IServiceCollection AddStockPortfolioRedis(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString(RedisConnectionStringName);

        if (string.IsNullOrWhiteSpace(redisConnectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{RedisConnectionStringName}' is not configured. Set "
                + $"ConnectionStrings__{RedisConnectionStringName} (compose and Bicep both do). MarketData "
                + "keeps the last known price of every symbol here, which is the dashboard's only fallback "
                + "when the quote provider is down, and the readiness probe reports on this connection.");
        }

        var redisOptions = ConfigurationOptions.Parse(redisConnectionString);

        // AbortOnConnectFail=false: a Redis blip must not kill startup.
        redisOptions.AbortOnConnectFail = false;

        // Resolved lazily: the singleton factory does not run until something asks for it.
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));

        return services;
    }
}
