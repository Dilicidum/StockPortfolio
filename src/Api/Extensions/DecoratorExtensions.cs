using StockPortfolio.Api.Decorators;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Api.Extensions;

/// <summary>Wraps every registered handler in the host's cross-cutting decorators.</summary>
public static class DecoratorExtensions
{
    /// <summary>Decorates every and already in the collection.</summary>
    public static IServiceCollection DecorateHandlers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingCommandHandler<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingQueryHandler<,>));

        return services;
    }
}
