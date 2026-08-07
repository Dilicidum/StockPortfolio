using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Alerts.Infrastructure;
using StockPortfolio.Modules.Identity.Infrastructure;
using StockPortfolio.Modules.MarketData.Infrastructure;
using StockPortfolio.Modules.Portfolio.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

internal static class ModuleDbContextInterceptors
{
    public static void AddToIdentity(IServiceCollection services, params IInterceptor[] interceptors) =>
        AddTo(services, IdentityDbContextType(), interceptors);

    public static void AddToPortfolio(IServiceCollection services, params IInterceptor[] interceptors) =>
        AddTo(services, PortfolioDbContextType(), interceptors);

    public static void AddToAlerts(IServiceCollection services, params IInterceptor[] interceptors) =>
        AddTo(services, AlertsDbContextType(), interceptors);

    public static void AddToMarketData(IServiceCollection services, params IInterceptor[] interceptors) =>
        AddTo(services, MarketDataDbContextType(), interceptors);

    public static Type AlertsDbContextType() => SingleDbContextIn(typeof(AlertsModule).Assembly);

    public static Type IdentityDbContextType() => SingleDbContextIn(typeof(IdentityModule).Assembly);

    public static Type PortfolioDbContextType() => SingleDbContextIn(typeof(PortfolioModule).Assembly);

    public static Type MarketDataDbContextType() => SingleDbContextIn(typeof(MarketDataModule).Assembly);

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
