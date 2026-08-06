using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Configurations;

/// <summary>Maps AlertSetting to alerts.alert_settings.</summary>
internal sealed class AlertSettingConfiguration : IEntityTypeConfiguration<AlertSetting>
{
    internal const string TableName = "alert_settings";

    /// <summary>A threshold belongs to a position, not to an account, and only the index can promise it.</summary>
    internal const string UserTickerUniqueIndexName = "ix_alert_settings_user_id_ticker";

    /// <summary>numeric(5,2) holds 100.00 and no more, which is exactly ThresholdPercent's range.</summary>
    private const int PercentPrecision = 5;

    private const int PercentScale = 2;

    public void Configure(EntityTypeBuilder<AlertSetting> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(setting => setting.Id);

        // The domain generates a UUIDv7 in AlertSettingId.New(); the database must not touch it.
        builder.Property(setting => setting.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(setting => setting.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(setting => setting.Ticker)
            .HasColumnName("ticker")
            .HasMaxLength(Ticker.MaxLength)
            .IsRequired();

        builder.Property(setting => setting.Enabled)
            .HasColumnName("enabled")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(setting => setting.Threshold)
            .HasColumnName("threshold_percent")
            .HasPrecision(PercentPrecision, PercentScale)
            .IsRequired();

        builder.Property(setting => setting.Window)
            .HasColumnName("window_minutes")
            .IsRequired();

        builder.HasIndex(setting => new { setting.UserId, setting.Ticker })
            .IsUnique()
            .HasDatabaseName(UserTickerUniqueIndexName);
    }
}
