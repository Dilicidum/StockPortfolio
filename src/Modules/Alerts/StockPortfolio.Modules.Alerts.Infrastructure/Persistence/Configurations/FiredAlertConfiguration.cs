using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.Alerts.Domain;

namespace StockPortfolio.Modules.Alerts.Infrastructure.Persistence.Configurations;

/// <summary>Maps FiredAlert to alerts.fired_alerts.</summary>
internal sealed class FiredAlertConfiguration : IEntityTypeConfiguration<FiredAlert>
{
    internal const string TableName = "fired_alerts";

    /// <summary>Newest first for one user is the only way the history endpoint reads this table.</summary>
    internal const string UserFiredAtIndexName = "ix_fired_alerts_user_id_fired_at";

    /// <summary>Fractional shares exist, so a price of $125.333333 must not round to $125.33.</summary>
    private const int MoneyPrecision = 18;

    private const int MoneyScale = 6;

    private const int CurrencyLength = 3;

    /// <summary>The same width as a price: a rise has no ceiling, so a percent column must not either.</summary>
    private const int PercentPrecision = 18;

    private const int PercentScale = 6;

    /// <summary>"Fall" and "Rise" are four; the room is for a third direction, not for free text.</summary>
    private const int DirectionLength = 8;

    public void Configure(EntityTypeBuilder<FiredAlert> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(alert => alert.Id);

        builder.Property(alert => alert.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(alert => alert.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(alert => alert.Ticker)
            .HasColumnName("ticker")
            .HasMaxLength(Ticker.MaxLength)
            .IsRequired();

        builder.Property(alert => alert.Direction)
            .HasColumnName("direction")
            .HasMaxLength(DirectionLength)
            .IsRequired();

        builder.Property(alert => alert.ChangePercent)
            .HasColumnName("change_percent")
            .HasPrecision(PercentPrecision, PercentScale)
            .IsRequired();

        builder.Property(alert => alert.EndpointPercent)
            .HasColumnName("endpoint_percent")
            .HasPrecision(PercentPrecision, PercentScale)
            .IsRequired();

        // Mapped member by member because Money's properties are get-only and are therefore not mapped
        // by convention - a bare ComplexProperty(a => a.TriggerPrice) maps nothing and then fails model
        // building with "No suitable constructor was found". Two of them, and neither shares a column.
        builder.ComplexProperty(alert => alert.TriggerPrice, price =>
        {
            price.Property(money => money.Amount)
                .HasColumnName("trigger_price_amount")
                .HasPrecision(MoneyPrecision, MoneyScale);

            price.Property(money => money.Currency)
                .HasColumnName("trigger_price_currency")
                .HasMaxLength(CurrencyLength)
                .IsFixedLength();
        });

        builder.ComplexProperty(alert => alert.ReferencePrice, price =>
        {
            price.Property(money => money.Amount)
                .HasColumnName("reference_price_amount")
                .HasPrecision(MoneyPrecision, MoneyScale);

            price.Property(money => money.Currency)
                .HasColumnName("reference_price_currency")
                .HasMaxLength(CurrencyLength)
                .IsFixedLength();
        });

        builder.Property(alert => alert.FiredAt)
            .HasColumnName("fired_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(alert => alert.IsSimulated)
            .HasColumnName("is_simulated")
            .IsRequired();

        builder.HasIndex(alert => new { alert.UserId, alert.FiredAt })
            .IsDescending(false, true)
            .HasDatabaseName(UserFiredAtIndexName);
    }
}
