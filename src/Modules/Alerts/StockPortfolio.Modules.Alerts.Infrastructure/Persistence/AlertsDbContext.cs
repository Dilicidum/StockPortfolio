using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Converters;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence;

/// <summary>The Alerts module's only DbContext.</summary>
internal sealed class AlertsDbContext(DbContextOptions<AlertsDbContext> options) : DbContext(options)
{
    /// <summary>The Postgres schema this context owns.</summary>
    internal const string SchemaName = "alerts";

    /// <summary>The migration history table name.</summary>
    internal const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<AlertSetting> AlertSettings => Set<AlertSetting>();

    public DbSet<FiredAlert> FiredAlerts => Set<FiredAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AlertsDbContext).Assembly,
            predicate: t => t.Namespace is not null
                && t.Namespace.StartsWith("StockPortfolio.Modules.Alerts", StringComparison.Ordinal));
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Throw(CoreEventId.SkippedEntityTypeConfigurationWarning));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Six converters, and every one is registered twice. DefaultTypeMapping is the line people miss:
        // without it a value used anywhere but a mapped property - a LINQ closure, say - has no mapping
        // and throws long after model building succeeded. Identity needs only the id half of this,
        // because it has no value object that is not an id.
        configurationBuilder.Properties<AlertSettingId>().HaveConversion<AlertSettingIdConverter>();
        configurationBuilder.DefaultTypeMapping<AlertSettingId>().HasConversion<AlertSettingIdConverter>();

        configurationBuilder.Properties<FiredAlertId>().HaveConversion<FiredAlertIdConverter>();
        configurationBuilder.DefaultTypeMapping<FiredAlertId>().HasConversion<FiredAlertIdConverter>();

        configurationBuilder.Properties<Ticker>().HaveConversion<TickerConverter>();
        configurationBuilder.DefaultTypeMapping<Ticker>().HasConversion<TickerConverter>();

        configurationBuilder.Properties<ThresholdPercent>().HaveConversion<ThresholdPercentConverter>();
        configurationBuilder.DefaultTypeMapping<ThresholdPercent>().HasConversion<ThresholdPercentConverter>();

        configurationBuilder.Properties<AlertWindow>().HaveConversion<AlertWindowConverter>();
        configurationBuilder.DefaultTypeMapping<AlertWindow>().HasConversion<AlertWindowConverter>();

        configurationBuilder.Properties<AlertDirection>().HaveConversion<AlertDirectionConverter>();
        configurationBuilder.DefaultTypeMapping<AlertDirection>().HasConversion<AlertDirectionConverter>();
    }
}
