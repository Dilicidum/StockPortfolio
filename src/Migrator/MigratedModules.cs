using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Identity.Infrastructure;
using StockPortfolio.Modules.Portfolio.Infrastructure;

namespace StockPortfolio.Migrator;

/// <summary>The one list of modules whose migrations are applied, and the one rule for finding their contexts.</summary>
public static class MigratedModules
{
    /// <summary>Registers every module that owns a schema. A module missing here never gets one created.</summary>
    public static IServiceCollection AddEveryMigratedModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // AddDbContext, never AddDbContextFactory: DbContextTypesIn only sees a context registered as
        // its own type. This runs against a bare ServiceCollection, so what is called here must be
        // self-contained - and must register persistence only, so nothing else needs configuring to migrate.
        services.AddIdentityPersistence(configuration);
        services.AddPortfolioModule(configuration);

        return services;
    }

    /// <summary>Lists the module contexts to migrate, ordered so two runs apply them in the same sequence.</summary>
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
