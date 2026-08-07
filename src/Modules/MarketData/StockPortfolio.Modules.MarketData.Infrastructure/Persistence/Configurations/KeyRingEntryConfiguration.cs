using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Persistence.Configurations;

internal sealed class KeyRingEntryConfiguration : IEntityTypeConfiguration<KeyRingEntry>
{
    internal const string TableName = "data_protection_keys";

    public void Configure(EntityTypeBuilder<KeyRingEntry> builder)
    {
        builder.ToTable(TableName);

        builder.Property(entry => entry.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.FriendlyName)
            .HasColumnName("friendly_name")
            .IsRequired();

        builder.Property(entry => entry.Xml)
            .HasColumnName("xml")
            .IsRequired();
    }
}
