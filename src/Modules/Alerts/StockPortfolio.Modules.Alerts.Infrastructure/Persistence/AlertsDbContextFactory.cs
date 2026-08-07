using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence;

internal sealed class AlertsDbContextFactory : IDesignTimeDbContextFactory<AlertsDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=stockportfolio;Username=migrator;Password=migrator_dev_only;Maximum Pool Size=2";

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

                npg => npg.MigrationsHistoryTable(
                    AlertsDbContext.MigrationsHistoryTableName,
                    AlertsDbContext.SchemaName))
            .Options;

        return new AlertsDbContext(options);
    }
}
