using StackExchange.Redis;

namespace StockPortfolio.Host.Extensions;

internal static class SignalRExtensions
{
    private const string BackplaneChannelPrefix = "stockportfolio:signalr";

    public static IServiceCollection AddStockPortfolioSignalR(
        this IServiceCollection services,
        ConfigurationOptions redisOptions)
    {
        // The backplane opens its own multiplexer rather than sharing the registered one; that is the documented shape.
        services.AddSignalR().AddStackExchangeRedis(options =>
        {
            // Cloned, not shared: the prefix below is the backplane's alone, and the application's multiplexer is built later than this runs.
            options.Configuration = redisOptions.Clone();
            options.Configuration.ChannelPrefix = RedisChannel.Literal(BackplaneChannelPrefix);
        });

        return services;
    }
}
