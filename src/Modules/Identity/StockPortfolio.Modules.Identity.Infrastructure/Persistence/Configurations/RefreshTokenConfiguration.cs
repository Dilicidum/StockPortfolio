using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="RefreshToken"/> to <c>identity.refresh_tokens</c>.</summary>
/// <remarks>
/// Parameterless constructor on purpose — see <see cref="UserConfiguration"/>.
/// </remarks>
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    internal const string TableName = "refresh_tokens";

    /// <summary>One row per opaque token. The uniqueness is what makes a hash lookup a point read.</summary>
    internal const string TokenHashUniqueIndexName = "ix_refresh_tokens_token_hash";

    /// <summary>
    /// Partial index over the same column, restricted to live rows. Rotation looks a token up by hash and
    /// then asks whether it is still active, so the index that answers it should never page in the
    /// superseded history — which is the majority of the table after a few days of use.
    /// </summary>
    internal const string ActiveTokenIndexName = "ix_refresh_tokens_active";

    /// <summary>SHA-256 output. Fixed width, so <c>bytea</c> comparisons never see a length mismatch.</summary>
    private const int TokenHashLength = 32;

    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

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

        // Null while the token is live. That nullability is the partial index's predicate.
        builder.Property(t => t.SupersededAt)
            .HasColumnName("superseded_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(t => t.SupersededBy)
            .HasColumnName("superseded_by");

        builder.HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName(TokenHashUniqueIndexName);

        // Second index on the same column, so the named overload is required — the unnamed one would
        // be treated as a redefinition of the unique index above.
        builder.HasIndex(t => t.TokenHash, ActiveTokenIndexName)
            .HasDatabaseName(ActiveTokenIndexName)
            .HasFilter("superseded_at IS NULL");

        // A real foreign key: both tables live in the `identity` schema and are owned by the same role,
        // so Postgres can enforce it. Cross-schema references (portfolio.holdings.user_id and friends)
        // stay logical-only — see docs/plan/er-diagram.md.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .HasConstraintName("fk_refresh_tokens_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(t => t.DomainEvents);
    }
}
