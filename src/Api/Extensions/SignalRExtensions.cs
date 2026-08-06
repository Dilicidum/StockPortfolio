using StackExchange.Redis;

namespace StockPortfolio.Api.Extensions;

/// <summary>SignalR and the one line that makes it work across replicas.</summary>
internal static class SignalRExtensions
{
    /// <summary>Keeps this app's backplane traffic off any other app sharing the same Redis.</summary>
    private const string BackplaneChannelPrefix = "stockportfolio:signalr";

    /// <summary>Registers SignalR with the Redis backplane, reusing the Redis connection string.</summary>
    public static IServiceCollection AddStockPortfolioSignalR(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Already proven present: AddStockPortfolioRedis throws on a missing value and runs first.
        var redisConnectionString = configuration.GetConnectionString(
            RedisExtensions.RedisConnectionStringName);

        // The backplane opens its own multiplexer rather than sharing the registered one. That is the
        // documented shape, and it is what lets an alert raised on one replica reach a browser holding
        // its connection on the other. maxReplicas is 2, so this is two Redis clients, not a fleet.
        services.AddSignalR().AddStackExchangeRedis(
            redisConnectionString!,
            options => options.Configuration.ChannelPrefix =
                RedisChannel.Literal(BackplaneChannelPrefix));

        return services;
    }
}
