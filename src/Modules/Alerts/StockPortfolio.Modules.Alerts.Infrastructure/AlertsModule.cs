using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Alerts.Application.Abstractions;
using StockPortfolio.Modules.Alerts.Infrastructure.Persistence;

namespace StockPortfolio.Modules.Alerts.Infrastructure;

/// <summary>The Alerts module's entire public surface to the host.</summary>
public static class AlertsModule
{
    /// <summary>The ConnectionStrings key this module reads.</summary>
    public const string ConnectionStringName = "Alerts";

    /// <summary>Registers the Alerts module: its DbContext and its two repositories.</summary>
    public static IServiceCollection AddAlertsModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        // The only eager check: a threshold is a stored thing, so Alerts genuinely cannot run without a
        // database. MarketData's missing Finnhub key is the opposite case and must not throw.
        var connectionString = config.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. Set "
                + $"ConnectionStrings:{ConnectionStringName} (or ConnectionStrings__{ConnectionStringName}). "
                + "Passing a null connection string to UseNpgsql throws later, from a stack that names "
                + "neither the key nor the file.");
        }

        // AddDbContext, never AddDbContextFactory: the Migrator discovers contexts by scanning service
        // descriptors for a DbContext subclass, and only the former registers the context as its own type.
        // MigrationsHistoryTable is set here as well as design-time - HasDefaultSchema does not move it,
        // so without this line all four modules share one history table (efcore#24127).
        services.AddDbContext<AlertsDbContext>(options => options.UseNpgsql(
            connectionString,
            npg => npg.MigrationsHistoryTable(
                AlertsDbContext.MigrationsHistoryTableName,
                AlertsDbContext.SchemaName)));

        services.AddScoped<IAlertSettingRepository, AlertSettingRepository>();
        services.AddScoped<IFiredAlertRepository, FiredAlertRepository>();

        return services;
    }
}
