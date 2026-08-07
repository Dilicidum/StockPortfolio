using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Alerts.Infrastructure;
using StockPortfolio.Modules.Identity.Infrastructure;
using StockPortfolio.Modules.MarketData.Infrastructure;
using StockPortfolio.Modules.Portfolio.Infrastructure;

namespace StockPortfolio.Migrator;

public static class MigratedModules
{
    /// <summary>A module missing from this list silently never gets its schema created.</summary>
    public static IServiceCollection AddEveryMigratedModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // AddDbContext, never AddDbContextFactory: DbContextTypesIn only finds a context registered as its own type, and this runs against a bare ServiceCollection.
        services.AddIdentityPersistence(configuration);
        services.AddPortfolioModule(configuration);
        services.AddAlertsModule(configuration);
        services.AddMarketDataPersistence(configuration);

        return services;
    }

    public static IReadOnlyList<Type> DbContextTypesIn(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return
        [
            .. services
                .Where(descriptor => descriptor.ServiceType.IsSubclassOf(typeof(DbContext)))
                .Select(descriptor => descriptor.ServiceType)
                .Distinct()
                .OrderBy(type => type.Name, StringComparer.Ordinal),
        ];
    }
}
