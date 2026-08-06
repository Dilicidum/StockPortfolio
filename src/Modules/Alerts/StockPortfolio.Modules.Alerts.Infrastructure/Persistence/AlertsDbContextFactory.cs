using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence;

/// <summary>Lets dotnet ef build an AlertsDbContext without booting the API host.</summary>
internal sealed class AlertsDbContextFactory : IDesignTimeDbContextFactory<AlertsDbContext>
{
    /// <summary>Matches the compose stack in docker-compose.yml, so the fallback is usable rather than decorative.</summary>
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=stockportfolio;Username=migrator;Password=migrator;Maximum Pool Size=2";

    private const string ConnectionStringEnvironmentVariable = "ConnectionStrings__Alerts";

    public AlertsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) is { Length: > 0 } fromEnvironment
                ? fromEnvironment
                : FallbackConnectionString;

        var options = new DbContextOptionsBuilder<AlertsDbContext>()
            .UseNpgsql(
                connectionString,

                // Repeated here as well as in AddAlertsModule. HasDefaultSchema does not move
                // __EFMigrationsHistory, so omitting this puts four contexts in one history table.
                npg => npg.MigrationsHistoryTable(
                    AlertsDbContext.MigrationsHistoryTableName,
                    AlertsDbContext.SchemaName))
            .Options;

        return new AlertsDbContext(options);
    }
}
