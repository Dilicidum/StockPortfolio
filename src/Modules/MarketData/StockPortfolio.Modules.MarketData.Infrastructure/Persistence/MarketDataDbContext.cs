using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Persistence;

/// <summary>The MarketData module's only DbContext.</summary>
internal sealed class MarketDataDbContext(DbContextOptions<MarketDataDbContext> options) : DbContext(options)
{
    /// <summary>The Postgres schema this context owns.</summary>
    internal const string SchemaName = "marketdata";

    /// <summary>The migration history table name.</summary>
    internal const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<UserProviderKey> UserProviderKeys => Set<UserProviderKey>();

    public DbSet<KeyRingEntry> KeyRingEntries => Set<KeyRingEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(MarketDataDbContext).Assembly,
            predicate: t => t.Namespace is not null
                && t.Namespace.StartsWith("StockPortfolio.Modules.MarketData", StringComparison.Ordinal));
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Throw(CoreEventId.SkippedEntityTypeConfigurationWarning));
    }
}
