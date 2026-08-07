using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence;

namespace StockPortfolio.Modules.Identity.Infrastructure;

public static class IdentityModule
{
    public const string ConnectionStringName = "Identity";

    public static IServiceCollection AddIdentityPersistence(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        var connectionString = config.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set "
                + $"ConnectionStrings:{ConnectionStringName} (or ConnectionStrings__{ConnectionStringName}). "
                + "Passing a null connection string to UseNpgsql throws later, from a stack that names "
                + "neither the key nor the file.");
        }

        // AddDbContext, never AddDbContextFactory: the Migrator finds contexts by their own service type.
        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(
            connectionString,
            npg => npg.MigrationsHistoryTable(
                IdentityDbContext.MigrationsHistoryTableName,
                IdentityDbContext.SchemaName)));

        services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>("postgres-identity");

        return services;
    }

    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.AddIdentityPersistence(config);

        services.AddIdentityCore<AppUser>()
            .AddEntityFrameworkStores<IdentityDbContext>();

        services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();

        services.AddIdentityHandlers();

        return services;
    }
}
