using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

/// <summary>The Identity module's only DbContext.</summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>The Postgres schema this context owns.</summary>
    internal const string SchemaName = "identity";

    /// <summary>The migration history table name.</summary>
    internal const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
        configurationBuilder.Properties<UserId>().HaveConversion<UserIdConverter>();
        configurationBuilder.DefaultTypeMapping<UserId>().HasConversion<UserIdConverter>();

        configurationBuilder.Properties<RefreshTokenId>().HaveConversion<RefreshTokenIdConverter>();
        configurationBuilder.DefaultTypeMapping<RefreshTokenId>().HasConversion<RefreshTokenIdConverter>();

        configurationBuilder.Properties<ThemeChoice>().HaveConversion<ThemeChoiceConverter>();
        configurationBuilder.DefaultTypeMapping<ThemeChoice>().HasConversion<ThemeChoiceConverter>();

        configurationBuilder.Properties<LanguageChoice>().HaveConversion<LanguageChoiceConverter>();
        configurationBuilder.DefaultTypeMapping<LanguageChoice>().HasConversion<LanguageChoiceConverter>();
    }
}
