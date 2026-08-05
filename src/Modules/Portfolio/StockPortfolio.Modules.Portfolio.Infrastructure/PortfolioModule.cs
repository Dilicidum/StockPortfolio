using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

namespace StockPortfolio.Modules.Portfolio.Infrastructure;

/// <summary>The Portfolio module's entire public surface to the host.</summary>
public static class PortfolioModule
{
    /// <summary>The ConnectionStrings key this module reads.</summary>
    public const string ConnectionStringName = "Portfolio";

    /// <summary>Registers the Portfolio module: its DbContext, repository, contract read, handlers.</summary>
    public static IServiceCollection AddPortfolioModule(this IServiceCollection services, IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(config);

        // The only eager check: Portfolio genuinely cannot run without a database. Nothing else is
        // validated here - Phase 3's missing Finnhub key is a supported state and must not throw.
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
        // so without this line all three modules share one history table (efcore#24127).
        services.AddDbContext<PortfolioDbContext>(options => options.UseNpgsql(
            connectionString,
            npg => npg.MigrationsHistoryTable(
                PortfolioDbContext.MigrationsHistoryTableName,
                PortfolioDbContext.SchemaName)));

        services.AddScoped<IHoldingRepository, HoldingRepository>();

        // The one read another module makes of Portfolio: does this user hold this ticker?
        services.AddScoped<IUserHoldsTicker, HoldingQueries>();

        services.AddPortfolioHandlers();

        return services;
    }
}
