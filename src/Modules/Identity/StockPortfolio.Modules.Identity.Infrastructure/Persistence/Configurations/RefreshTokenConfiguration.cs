using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Configurations;

/// <summary>Maps RefreshToken to identity.refresh_tokens.</summary>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    internal const string TableName = "refresh_tokens";

    /// <summary>One row per opaque token, and the index every refresh look-up goes through.</summary>
    internal const string TokenHashUniqueIndexName = "ix_refresh_tokens_token_hash";

    /// <summary>EF creates an index for the foreign key whether or not it is asked to, so it is named here.</summary>
    internal const string UserIdIndexName = "ix_refresh_tokens_user_id";

    /// <summary>SHA-256 output.</summary>
    private const int TokenHashLength = 32;

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(t => t.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(t => t.TokenHash)
            .HasColumnName("token_hash")
            .HasColumnType("bytea")
            .HasMaxLength(TokenHashLength)
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Null while the token is live.
        builder.Property(t => t.SupersededAt)
            .HasColumnName("superseded_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(t => t.SupersededBy)
            .HasColumnName("superseded_by");

        builder.HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName(TokenHashUniqueIndexName);

        builder.HasIndex(t => t.UserId)
            .HasDatabaseName(UserIdIndexName);

        // A real foreign key: both tables live in the `identity` schema and are owned by the same role.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .HasConstraintName("fk_refresh_tokens_user_id")
            .OnDelete(DeleteBehavior.Cascade);

    }
}
