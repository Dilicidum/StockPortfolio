using StockPortfolio.Api.Decorators;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Api.Extensions;

/// <summary>
/// Wraps every registered handler in the host's cross-cutting decorators.
/// </summary>
/// <remarks>
/// <para>
/// Logging only, in Phase 1. Validation is an <c>IEndpointFilter</c> rather than a decorator, and
/// transactions belong to the handlers' own <c>IUnitOfWork</c>; adding a decorator per concern
/// "because that is the pattern" would buy indirection and nothing else.
/// </para>
/// <para>
/// <c>Decorate</c>, not <c>TryDecorate</c>. The <c>Try</c> form returns <see langword="false"/> when
/// nothing matched and would turn a module that silently stopped registering its handlers into a host
/// that silently stopped logging them. This throws instead.
/// </para>
/// </remarks>
public static class DecoratorExtensions
{
    /// <summary>
    /// Decorates every <see cref="ICommandHandler{TCommand, TResult}"/> and
    /// <see cref="IQueryHandler{TQuery, TResult}"/> already in the collection.
    /// </summary>
    /// <param name="services">The service collection to decorate.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Must be called <b>after</b> the modules register their handlers — a decorator only applies to
    /// descriptors that already exist.
    /// </remarks>
    public static IServiceCollection DecorateHandlers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Decorate(typeof(ICommandHandler<,>), typeof(LoggingCommandHandler<,>));
        services.Decorate(typeof(IQueryHandler<,>), typeof(LoggingQueryHandler<,>));

        return services;
    }
}
