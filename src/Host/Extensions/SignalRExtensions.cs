using StackExchange.Redis;

namespace StockPortfolio.Host.Extensions;

internal static class SignalRExtensions
{
    private const string BackplaneChannelPrefix = "stockportfolio:signalr";

    public static IServiceCollection AddStockPortfolioSignalR(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Already proven present: AddStockPortfolioRedis throws on a missing value and runs first.
        var redisConnectionString = configuration.GetConnectionString(
            RedisExtensions.RedisConnectionStringName);

        // The backplane opens its own multiplexer rather than sharing the registered one; that is the documented shape.
        services.AddSignalR().AddStackExchangeRedis(
            redisConnectionString!,
            options => options.Configuration.ChannelPrefix =
                RedisChannel.Literal(BackplaneChannelPrefix));

        return services;
    }
}
