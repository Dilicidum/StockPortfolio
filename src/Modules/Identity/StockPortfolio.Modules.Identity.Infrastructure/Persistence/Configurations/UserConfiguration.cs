using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Configurations;

/// <summary>Maps User to identity.users.</summary>
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    internal const string TableName = "users";

    /// <summary>The unique index behind registration conflict detection.</summary>
    internal const string EmailUniqueIndexName = "ix_users_email";

    /// <summary>RFC 5321 caps a forward path at 320 characters.</summary>
    private const int EmailMaxLength = 320;

    /// <summary>An argon2id PHC string with a 16-byte salt and 32-byte hash is ~100 chars; 256 leaves room to raise.</summary>
    private const int PasswordHashMaxLength = 256;

    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(u => u.Id);

        // The domain generates a UUIDv7 in UserId.New(); the database must not touch it.
        builder.Property(u => u.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(EmailMaxLength)
            .IsRequired();

        builder.Property(u => u.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(PasswordHashMaxLength)
            .IsRequired();

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasDatabaseName(EmailUniqueIndexName);

    }
}
