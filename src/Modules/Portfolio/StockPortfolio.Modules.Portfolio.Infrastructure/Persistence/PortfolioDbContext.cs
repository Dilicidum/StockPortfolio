using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Converters;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence;

/// <summary>The Portfolio module's only DbContext.</summary>
internal sealed class PortfolioDbContext(DbContextOptions<PortfolioDbContext> options) : DbContext(options)
{
    /// <summary>The Postgres schema this context owns.</summary>
    internal const string SchemaName = "portfolio";

    /// <summary>The migration history table name.</summary>
    internal const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<Holding> Holdings => Set<Holding>();

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

        // DefaultTypeMapping is the line people miss: without it a Ticker used anywhere but a mapped
        // property - a LINQ closure, say - has no mapping and throws long after model building succeeded.
        configurationBuilder.Properties<Ticker>().HaveConversion<TickerConverter>();
        configurationBuilder.DefaultTypeMapping<Ticker>().HasConversion<TickerConverter>();
    }
}
