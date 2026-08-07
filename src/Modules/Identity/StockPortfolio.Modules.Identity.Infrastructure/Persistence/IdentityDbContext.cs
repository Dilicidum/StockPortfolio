using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options)
{
    internal const string SchemaName = "identity";

    internal const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // First and not optional: this is what maps AspNetUsers and its three siblings.
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(IdentityDbContext).Assembly,
            predicate: t => t.Namespace is not null
                && t.Namespace.StartsWith("StockPortfolio.Modules.Identity", StringComparison.Ordinal));
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.ConfigureWarnings(w => w.Throw(CoreEventId.SkippedEntityTypeConfigurationWarning));
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<ThemeChoice>().HaveConversion<ThemeChoiceConverter>();
        configurationBuilder.DefaultTypeMapping<ThemeChoice>().HasConversion<ThemeChoiceConverter>();

        configurationBuilder.Properties<LanguageChoice>().HaveConversion<LanguageChoiceConverter>();
        configurationBuilder.DefaultTypeMapping<LanguageChoice>().HasConversion<LanguageChoiceConverter>();
    }
}
