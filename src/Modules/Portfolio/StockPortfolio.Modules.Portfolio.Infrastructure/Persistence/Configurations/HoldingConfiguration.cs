using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Configurations;

internal sealed class HoldingConfiguration : IEntityTypeConfiguration<Holding>
{
    internal const string TableName = "holdings";

    internal const string UserTickerUniqueIndexName = "ix_holdings_user_id_ticker";

    private const int MoneyPrecision = 18;

    private const int MoneyScale = 6;

    private const int CurrencyLength = 3;

    public void Configure(EntityTypeBuilder<Holding> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(h => h.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(h => h.Ticker)
            .HasColumnName("ticker")
            .HasMaxLength(Ticker.MaxLength)
            .IsRequired();

        builder.Property(h => h.Quantity)
            .HasColumnName("quantity")
            .HasPrecision(MoneyPrecision, MoneyScale)
            .IsRequired();

        builder.ComplexProperty(h => h.AveragePrice, price =>
        {
            price.Property(m => m.Amount)
                .HasColumnName("avg_price_amount")
                .HasPrecision(MoneyPrecision, MoneyScale);

            price.Property(m => m.Currency)
                .HasColumnName("avg_price_currency")
                .HasMaxLength(CurrencyLength)
                .IsFixedLength();
        });

        builder.Property(h => h.IsVisible)
            .HasColumnName("is_visible")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(h => h.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(h => h.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(h => new { h.UserId, h.Ticker })
            .IsUnique()
            .HasDatabaseName(UserTickerUniqueIndexName);
    }
}
