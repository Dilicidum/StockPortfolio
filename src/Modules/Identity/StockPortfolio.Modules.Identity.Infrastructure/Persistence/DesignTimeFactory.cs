using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

/// <summary>Lets dotnet ef build an IdentityDbContext without booting the API host.</summary>
internal sealed class DesignTimeIdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    /// <summary>Matches the compose stack in docker-compose.yml, so the fallback is usable rather than decorative.</summary>
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=stockportfolio;Username=migrator;Password=migrator;Maximum Pool Size=2";

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
