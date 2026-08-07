using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

internal sealed class PortfolioDbContextFactory : IDesignTimeDbContextFactory<PortfolioDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=stockportfolio;Username=migrator;Password=migrator_dev_only;Maximum Pool Size=2";

    private const string ConnectionStringEnvironmentVariable = "ConnectionStrings__Portfolio";

    public PortfolioDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) is { Length: > 0 } fromEnvironment
                ? fromEnvironment
                : FallbackConnectionString;

        var options = new DbContextOptionsBuilder<PortfolioDbContext>()
            .UseNpgsql(
                connectionString,
                npg => npg.MigrationsHistoryTable(
                    PortfolioDbContext.MigrationsHistoryTableName,
                    PortfolioDbContext.SchemaName))
            .Options;

        return new PortfolioDbContext(options);
    }
}
