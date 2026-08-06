using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using StockPortfolio.Modules.Portfolio.Domain;

namespace StockPortfolio.Modules.Portfolio.Infrastructure.Persistence.Configurations;

// Maps DashboardSettings to portfolio.dashboard_settings.
internal sealed class DashboardSettingsConfiguration : IEntityTypeConfiguration<DashboardSettings>
{
    public void Configure(EntityTypeBuilder<DashboardSettings> builder)
    {
        builder.ToTable("dashboard_settings");

        builder.HasKey(s => s.UserId);
        builder.Property(s => s.UserId).HasColumnName("user_id").ValueGeneratedNever();

        // No foreign key: Portfolio does not own the users table and has no grant across that schema boundary.
        builder.Property(s => s.RefreshInterval).HasColumnName("refresh_interval_seconds").IsRequired();
    }
}
