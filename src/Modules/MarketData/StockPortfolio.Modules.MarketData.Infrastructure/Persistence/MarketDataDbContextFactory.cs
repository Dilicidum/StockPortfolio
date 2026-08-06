using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Persistence;

/// <summary>Lets dotnet ef build a MarketDataDbContext without booting the API host.</summary>
internal sealed class MarketDataDbContextFactory : IDesignTimeDbContextFactory<MarketDataDbContext>
{
    /// <summary>Matches the compose stack in docker-compose.yml, so the fallback is usable rather than decorative.</summary>
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=stockportfolio;Username=migrator;Password=migrator;Maximum Pool Size=2";

    private const string ConnectionStringEnvironmentVariable = "ConnectionStrings__MarketData";

    public MarketDataDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) is { Length: > 0 } fromEnvironment
                ? fromEnvironment
                : FallbackConnectionString;

        var options = new DbContextOptionsBuilder<MarketDataDbContext>()
            .UseNpgsql(
                connectionString,

                // Repeated here as well as in AddMarketDataPersistence. HasDefaultSchema does not move
                // __EFMigrationsHistory, so omitting this puts four contexts in one history table.
                npg => npg.MigrationsHistoryTable(
                    MarketDataDbContext.MigrationsHistoryTableName,
                    MarketDataDbContext.SchemaName))
            .Options;

        return new MarketDataDbContext(options);
    }
}
