using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

internal sealed class DesignTimeIdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=stockportfolio;Username=migrator;Password=migrator_dev_only;Maximum Pool Size=2";

    private const string ConnectionStringEnvironmentVariable = "ConnectionStrings__Identity";

    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) is { Length: > 0 } fromEnvironment
                ? fromEnvironment
                : FallbackConnectionString;

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                connectionString,
                npg => npg.MigrationsHistoryTable(
                    IdentityDbContext.MigrationsHistoryTableName,
                    IdentityDbContext.SchemaName))
            .Options;

        return new IdentityDbContext(options);
    }
}
