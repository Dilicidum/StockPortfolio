using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence;

namespace StockPortfolio.Modules.Identity.Infrastructure;

/// <summary>The Identity module's entire public surface to the host.</summary>
public static class IdentityModule
{
    /// <summary>The ConnectionStrings key this module reads.</summary>
    public const string ConnectionStringName = "Identity";

    /// <summary>Registers only the Identity DbContext, for the migrator.</summary>
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

        // This module's own readiness check. It lives here and not in the host because IdentityDbContext
        // is internal, and it borrows the scoped context rather than opening a connection, which is
        // what keeps four checks inside a Maximum Pool Size=2 budget.
        services.AddHealthChecks().AddDbContextCheck<IdentityDbContext>("postgres-identity");

        return services;
    }

    /// <summary>Registers the module's EF store and preferences. The host adds the endpoints and the tokens.</summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        services.AddIdentityPersistence(config);

        // The EF half of Identity only. AddIdentityApiEndpoints and the bearer scheme live in the host,
        // because both are ASP.NET Core and this assembly may not reference the web stack.
        services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<IdentityDbContext>();

        services.AddScoped<IUserPreferencesRepository, UserPreferencesRepository>();

        services.AddIdentityHandlers();

        return services;
    }
}
