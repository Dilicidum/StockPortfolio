using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.Alerts.Domain;
using StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Converters;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence;

internal sealed class AlertsDbContext(DbContextOptions<AlertsDbContext> options) : DbContext(options)
{
    internal const string SchemaName = "alerts";

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
