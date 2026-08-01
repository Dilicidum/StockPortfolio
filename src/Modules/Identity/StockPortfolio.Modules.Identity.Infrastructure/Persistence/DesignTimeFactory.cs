using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockPortfolio.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build an <see cref="IdentityDbContext"/> without booting the API host.
/// </summary>
/// <remarks>
/// <para>
/// Without this, <c>dotnet ef migrations add --startup-project src/Api</c> builds and runs the host,
/// which calls <c>UseNpgsql(config.GetConnectionString("Identity"))</c>. If that key is absent from the
/// local <c>appsettings.Development.json</c> the argument is null and Npgsql throws an
/// <see cref="ArgumentException"/> naming neither the configuration key nor the file — a genuinely
/// confusing half hour. EF prefers a design-time factory over the host, so this short-circuits it.
/// </para>
/// <para>
/// The connection string is never opened by <c>migrations add</c>; the provider only needs it to pick a
/// SQL dialect. <c>migrations script</c> and <c>database update</c> do connect, so
/// <c>ConnectionStrings__Identity</c> is honoured when it is set.
/// </para>
/// </remarks>
internal sealed class DesignTimeIdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    /// <summary>Matches the compose stack in <c>docker-compose.yml</c>, so the fallback is usable rather than decorative.</summary>
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=stockportfolio;Username=migrator;Password=migrator;Maximum Pool Size=2";

    private const string ConnectionStringEnvironmentVariable = "ConnectionStrings__Identity";

    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable) is { Length: > 0 } fromEnvironment
                ? fromEnvironment
                : FallbackConnectionString;

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(
                connectionString,
                npg => npg.MigrationsHistoryTable(
                    IdentityDbContext.MigrationsHistoryTableName,
                    IdentityDbContext.SchemaName))
            .Options;

        return new IdentityDbContext(options);
    }
}
