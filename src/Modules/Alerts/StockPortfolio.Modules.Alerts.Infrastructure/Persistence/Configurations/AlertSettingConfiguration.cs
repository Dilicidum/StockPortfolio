using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Configurations;

internal sealed class AlertSettingConfiguration : IEntityTypeConfiguration<AlertSetting>
{
    internal const string TableName = "alert_settings";

    internal const string UserTickerUniqueIndexName = "ix_alert_settings_user_id_ticker";

    private const int PercentPrecision = 5;

    private const int PercentScale = 2;

    public void Configure(EntityTypeBuilder<AlertSetting> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(setting => setting.Id);

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
