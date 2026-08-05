using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Identity.Infrastructure;
using StockPortfolio.Modules.Portfolio.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>Attaches an EF Core interceptor to a module's DbContext from outside the module.</summary>
internal static class ModuleDbContextInterceptors
{
    /// <summary>Adds interceptors to the Identity module's DbContext registration.</summary>
    public static void AddToIdentity(IServiceCollection services, params IInterceptor[] interceptors) =>
        AddTo(services, IdentityDbContextType(), interceptors);

    /// <summary>Adds interceptors to the Portfolio module's DbContext registration.</summary>
    public static void AddToPortfolio(IServiceCollection services, params IInterceptor[] interceptors) =>
        AddTo(services, PortfolioDbContextType(), interceptors);

    /// <summary>Finds the single DbContext declared by Identity.Infrastructure.</summary>
    public static Type IdentityDbContextType() => SingleDbContextIn(typeof(IdentityModule).Assembly);

    /// <summary>Finds the single DbContext declared by Portfolio.Infrastructure.</summary>
    public static Type PortfolioDbContextType() => SingleDbContextIn(typeof(PortfolioModule).Assembly);

    private static Type SingleDbContextIn(Assembly assembly)
    {
        var candidates = assembly
            .GetTypes()
            .Where(type => typeof(DbContext).IsAssignableFrom(type) && !type.IsAbstract)
            .ToArray();

        return candidates.Length == 1
            ? candidates[0]
            : throw new InvalidOperationException(
                $"Expected exactly one DbContext in {assembly.GetName().Name}, found "
                + $"{candidates.Length}. The integration tests reach the context by reflection because it is "
                + "internal to the module; update ModuleDbContextInterceptors if the module grew a second one.");
    }

    private static void AddTo(IServiceCollection services, Type contextType, IInterceptor[] interceptors)
    {
        ArgumentNullException.ThrowIfNull(services);

        var attach = typeof(ModuleDbContextInterceptors)
            .GetMethod(nameof(AddToContext), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(contextType);

        attach.Invoke(null, [services, interceptors]);
    }

    private static void AddToContext<TContext>(IServiceCollection services, IInterceptor[] interceptors)
        where TContext : DbContext
    {
        var descriptor = services.LastOrDefault(service => service.ServiceType == typeof(DbContextOptions<TContext>))
            ?? throw new InvalidOperationException(
                $"No DbContextOptions<{typeof(TContext).Name}> is registered. The module's Add…Module call "
                + "must run before the interceptor is attached — ConfigureTestServices runs after the "
                + "application's own registrations, which is why it is the right hook.");

        var original = descriptor.ImplementationFactory
            ?? throw new InvalidOperationException(
                $"DbContextOptions<{typeof(TContext).Name}> is registered without an implementation factory. "
                + "AddDbContext always registers one; a different registration shape means this helper is "
                + "wrapping the wrong descriptor.");

        services.Remove(descriptor);

        services.Add(new ServiceDescriptor(
            typeof(DbContextOptions<TContext>),
            provider => new DbContextOptionsBuilder<TContext>((DbContextOptions<TContext>)original(provider))
                .AddInterceptors(interceptors)
                .Options,
            descriptor.Lifetime));
    }
}
