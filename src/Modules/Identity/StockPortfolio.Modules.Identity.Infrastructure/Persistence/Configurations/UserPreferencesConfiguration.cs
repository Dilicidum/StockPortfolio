using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence.Configurations;

// Maps UserPreferences to identity.user_preferences. The only table this module still owns:
// everything else in the schema is the framework's.
internal sealed class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> builder)
    {
        builder.ToTable("user_preferences");

        builder.HasKey(p => p.UserId);
        builder.Property(p => p.UserId).HasColumnName("user_id").ValueGeneratedNever();

        builder.Property(p => p.Theme).HasColumnName("theme").HasMaxLength(16).IsRequired();
        builder.Property(p => p.Language).HasColumnName("language").HasMaxLength(16).IsRequired();

        builder.HasOne<IdentityUser>()
            .WithOne()
            .HasForeignKey<UserPreferences>(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
