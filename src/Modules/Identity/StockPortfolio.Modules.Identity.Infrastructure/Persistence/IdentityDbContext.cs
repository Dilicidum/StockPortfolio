using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Modules.Identity.Infrastructure.Persistence.Converters;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// The Identity module's only <see cref="DbContext"/>. It owns the <c>identity</c> Postgres schema and
/// connects as the <c>identity_svc</c> role, which has no <c>USAGE</c> on any other module's schema.
/// </summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>The Postgres schema this context owns.</summary>
    internal const string SchemaName = "identity";

    /// <summary>
    /// The migration history table name. It must be paired with <see cref="SchemaName"/> at
    /// <c>UseNpgsql(..., npg =&gt; npg.MigrationsHistoryTable(...))</c> — see the remarks on
    /// <see cref="OnModelCreating"/>.
    /// </summary>
    internal const string MigrationsHistoryTableName = "__EFMigrationsHistory";

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <remarks>
    /// <para>
    /// <c>HasDefaultSchema</c> moves the tables but <b>not</b> <c>__EFMigrationsHistory</c>
    /// (efcore#24127, closed <i>not planned</i>). The history table is placed by
    /// <c>MigrationsHistoryTable("__EFMigrationsHistory", "identity")</c> on the Npgsql options
    /// builder instead — see <c>IdentityModule.AddIdentityModule</c> and
    /// <see cref="DesignTimeIdentityDbContextFactory"/>. Without it all four module contexts share
    /// <c>public.__EFMigrationsHistory</c>, each sees the others' migration ids, and
    /// <c>database update</c> reports migrations as applied-but-missing. It looks like data corruption.
    /// </para>
    /// <para>
    /// The assembly scan is filtered by namespace. <c>ApplyConfigurationsFromAssembly</c> silently
    /// skips any <c>IEntityTypeConfiguration</c> whose constructor takes parameters, logging a warning
    /// nobody reads; <see cref="OnConfiguring"/> turns that warning into an exception.
    /// </para>
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(IdentityDbContext).Assembly,
            predicate: t => t.Namespace is not null
                && t.Namespace.StartsWith("StockPortfolio.Modules.Identity", StringComparison.Ordinal));
    }

    /// <remarks>
    /// Deliberately unconditional rather than Development-only: the assembly being scanned is the same
    /// in every environment, so a configuration that is skipped in production would have been skipped in
    /// development too. Throwing turns a mysteriously unmapped table into a startup failure everywhere.
    /// </remarks>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        optionsBuilder.ConfigureWarnings(w => w.Throw(CoreEventId.SkippedEntityTypeConfigurationWarning));
    }

    /// <remarks>
    /// Both lines are required for every strongly-typed id.
    /// <c>Properties&lt;T&gt;()</c> covers mapped entity properties; <c>DefaultTypeMapping&lt;T&gt;()</c>
    /// covers every other use — a value in a <c>Where</c> clause, a raw parameter, a projection. Miss the
    /// second and the model builds fine and then throws at runtime, far from the cause.
    /// </remarks>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<UserId>().HaveConversion<UserIdConverter>();
        configurationBuilder.DefaultTypeMapping<UserId>().HasConversion<UserIdConverter>();

        configurationBuilder.Properties<RefreshTokenId>().HaveConversion<RefreshTokenIdConverter>();
        configurationBuilder.DefaultTypeMapping<RefreshTokenId>().HasConversion<RefreshTokenIdConverter>();
    }
}
