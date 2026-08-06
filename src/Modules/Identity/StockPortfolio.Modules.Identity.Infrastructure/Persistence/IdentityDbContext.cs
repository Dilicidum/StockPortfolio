using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

/// <summary>The Identity module's only DbContext: the framework's four user tables plus this module's one.</summary>
/// <remarks>
/// IdentityUserContext, not IdentityDbContext. The difference is roles: IdentityDbContext would also map
/// AspNetRoles, AspNetUserRoles and AspNetRoleClaims, and this app has no concept of a role. Three tables
/// that can only ever be empty are three tables someone will eventually try to use.
/// </remarks>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityUserContext<AppUser, Guid>(options)
{
    /// <summary>The Postgres schema this context owns.</summary>
    internal const string SchemaName = "identity";

    /// <summary>The migration history table name.</summary>
    internal const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // First, and not optional: this is what maps AspNetUsers and its three siblings. Skip it and the
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
