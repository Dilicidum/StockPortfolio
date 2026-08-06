using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

// The framework's base class has the same short name as this one. Aliasing it is the only way to write
// the base list without a fully-qualified type on the declaration line.
using AspNetIdentityDbContext =
    Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext<Microsoft.AspNetCore.Identity.IdentityUser>;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

/// <summary>The Identity module's only DbContext: the framework's seven tables plus this module's one.</summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : AspNetIdentityDbContext(options)
{
    /// <summary>The Postgres schema this context owns.</summary>
    internal const string SchemaName = "identity";

    /// <summary>The migration history table name.</summary>
    internal const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // First, and not optional: this is what maps AspNetUsers and its six siblings. Skip it and the
        // model builds with only user_preferences in it, and UserManager fails on the first query.
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
