using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Modules.Portfolio.Contracts;
using StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

namespace StockPortfolio.Modules.Portfolio.Infrastructure;

public static class PortfolioModule
{
    public const string ConnectionStringName = "Portfolio";

    public static IServiceCollection AddPortfolioModule(this IServiceCollection services, IConfiguration config)
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
        services.AddDbContext<PortfolioDbContext>(options => options.UseNpgsql(
            connectionString,
            npg =>
            {
                npg.MigrationsHistoryTable(
                    PortfolioDbContext.MigrationsHistoryTableName,
                    PortfolioDbContext.SchemaName);

                npg.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorCodesToAdd: null);
            }));

        services.AddHealthChecks().AddDbContextCheck<PortfolioDbContext>("postgres-portfolio", tags: ["ready", "detail"]);

        services.AddScoped<IHoldingRepository, HoldingRepository>();
        services.AddScoped<IDashboardSettingsRepository, DashboardSettingsRepository>();

        services.AddScoped<IUserHoldsTicker, HoldingQueries>();

        services.AddScoped<IDashboardHoldingReader, HoldingQueries>();

        services.AddPortfolioHandlers();

        return services;
    }
}
