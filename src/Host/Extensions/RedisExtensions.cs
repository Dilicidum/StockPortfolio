using StackExchange.Redis;

namespace StockPortfolio.Host.Extensions;

internal static class RedisExtensions
{
    public const string RedisConnectionStringName = "Redis";

    /// <summary>Parsed here and nowhere else, so the SignalR backplane's own connection inherits these settings.</summary>
    public static ConfigurationOptions ReadConnectionOptions(IConfiguration configuration)
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

        // A Redis blip must not kill startup.
        redisOptions.AbortOnConnectFail = false;

        return redisOptions;
    }

    public static IServiceCollection AddStockPortfolioRedis(
        this IServiceCollection services,
        ConfigurationOptions redisOptions)
    {
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));

        return services;
    }
}
