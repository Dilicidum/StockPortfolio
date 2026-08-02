using StockPortfolio.Api.Decorators;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Api.Extensions;

/// <summary>Wraps every registered handler in the host's cross-cutting decorators.</summary>
internal static class DecoratorExtensions
{
    /// <summary>Wraps every command and query handler already registered in logging.</summary>
    public static IServiceCollection DecorateHandlers(this IServiceCollection services)
    {
        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingCommandHandler<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingQueryHandler<,>));

        return services;
    }
}
