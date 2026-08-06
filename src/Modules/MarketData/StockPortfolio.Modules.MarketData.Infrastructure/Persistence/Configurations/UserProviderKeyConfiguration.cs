using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Persistence.Configurations;

/// <summary>Maps UserProviderKey to marketdata.user_provider_keys.</summary>
internal sealed class UserProviderKeyConfiguration : IEntityTypeConfiguration<UserProviderKey>
{
    internal const string TableName = "user_provider_keys";

    private const int LastFourLength = 4;

    public void Configure(EntityTypeBuilder<UserProviderKey> builder)
    {
        builder.ToTable(TableName);

        // One key per user, so the user id is the key itself rather than a separate surrogate id.
        builder.HasKey(key => key.UserId);

        builder.Property(key => key.UserId)
            .HasColumnName("user_id")
            .ValueGeneratedNever();

        builder.Property(key => key.Ciphertext)
            .HasColumnName("ciphertext")
            .IsRequired();

        builder.Property(key => key.LastFour)
            .HasColumnName("last_four")
            .HasMaxLength(LastFourLength)
            .IsRequired();

        builder.Property(key => key.SavedAt)
            .HasColumnName("saved_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(key => key.LastRejectedAt)
            .HasColumnName("last_rejected_at")
            .HasColumnType("timestamp with time zone");
    }
}
