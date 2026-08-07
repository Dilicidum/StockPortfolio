using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Converters;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

internal sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : DbContext(options)
{
    internal const string SchemaName = "portfolio";

    internal const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<Holding> Holdings => Set<Holding>();

    public DbSet<DashboardSettings> DashboardSettings => Set<DashboardSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(PortfolioDbContext).Assembly,
            predicate: t => t.Namespace is not null
                && t.Namespace.StartsWith("StockPortfolio.Modules.Portfolio", StringComparison.Ordinal));
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Throw(CoreEventId.SkippedEntityTypeConfigurationWarning));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<HoldingId>().HaveConversion<HoldingIdConverter>();
        configurationBuilder.DefaultTypeMapping<HoldingId>().HasConversion<HoldingIdConverter>();

        configurationBuilder.Properties<Ticker>().HaveConversion<TickerConverter>();
        configurationBuilder.DefaultTypeMapping<Ticker>().HasConversion<TickerConverter>();

        configurationBuilder.Properties<RefreshInterval>().HaveConversion<RefreshIntervalConverter>();
        configurationBuilder.DefaultTypeMapping<RefreshInterval>().HasConversion<RefreshIntervalConverter>();
    }
}
