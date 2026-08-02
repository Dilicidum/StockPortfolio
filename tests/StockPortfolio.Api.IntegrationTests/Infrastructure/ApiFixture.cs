using System.Globalization;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>The one Postgres container, the one Redis container and the one API host that every integration.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    /// <summary>Matches docker-compose.yml, so the tests exercise the same server version that ships.</summary>
    private const string PostgresImage = "postgres:18-alpine";

    private const string RedisImage = "redis:8-alpine";

    private const string DatabaseName = "stockportfolio";
    private const string SuperUserName = "postgres";
    private const string SuperUserPassword = "postgres_test_only";

    /// <summary>The DDL role.</summary>
    public const string MigratorRole = "migrator";

    /// <summary>The Identity module's DML role.</summary>
    public const string IdentityRole = "identity_svc";

    /// <summary>The Portfolio module's DML role.</summary>
    public const string PortfolioRole = "portfolio_svc";

    private const string RolePassword = "role_test_only";

    /// <summary>A 46-byte key.</summary>
    private const string SigningKey = "integration-test-signing-key-not-a-secret-0123";

    private const string CorsOrigin = "http://localhost:5173";

    private readonly PostgreSqlContainer _postgres;
    private readonly RedisContainer _redis;

    private ApiFactory? _api;

    /// <summary>Creates the fixture.</summary>
    public ApiFixture()
    {
        GuardAgainstWindowsLineEndings();

        _postgres = new PostgreSqlBuilder(PostgresImage)
            .WithUsername(SuperUserName)
            .WithPassword(SuperUserPassword)
            .WithDatabase(DatabaseName)

            // postgres:18 moved its default data directory under /var/lib/postgresql/<major>/.
            .WithEnvironment("PGDATA", "/var/lib/postgresql/data/pgdata")

            // Consumed by 00-roles.sh and passed to psql as -v variables.
            .WithEnvironment("MIGRATOR_PW", RolePassword)
            .WithEnvironment("IDENTITY_PW", RolePassword)
            .WithEnvironment("PORTFOLIO_PW", RolePassword)
            .WithEnvironment("MARKETDATA_PW", RolePassword)
            .WithEnvironment("ALERTS_PW", RolePassword)

            .WithBindMount(
                RepositoryPaths.RolesShellScript,
                "/docker-entrypoint-initdb.d/00-roles.sh",
                AccessMode.ReadOnly)
            .WithBindMount(RepositoryPaths.DatabaseInitDirectory, "/db/init", AccessMode.ReadOnly)

            .WithWaitStrategy(Wait.ForUnixContainer().AddCustomWaitStrategy(
                new DatabaseInitialisedWaitStrategy(container => ConnectionStringFor(
                    SuperUserName,
                    SuperUserPassword,
                    container.Hostname,
                    container.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort)))))
            .Build();

        _redis = new RedisBuilder(RedisImage).Build();
    }

    /// <summary>Gets every SQL statement the shared host has executed.</summary>
    public RecordingDbCommandInterceptor RecordedCommands { get; } = new();

    /// <summary>Gets the shared host's service provider.</summary>
    public IServiceProvider Services => Api.Services;

    /// <summary>Gets the connection string for the migrator role, which owns the four schemas.</summary>
    public string MigratorConnectionString => ConnectionStringFor(MigratorRole, RolePassword);

    /// <summary>Gets the connection string the API itself uses.</summary>
    public string IdentityConnectionString => ConnectionStringFor(IdentityRole, RolePassword);

    /// <summary>Gets the connection string for the Portfolio role, which must not see identity.</summary>
    public string PortfolioConnectionString => ConnectionStringFor(PortfolioRole, RolePassword);

    private ApiFactory Api => _api ?? throw new InvalidOperationException("The fixture has not been initialised.");

    /// <summary>Creates a client against the shared host.</summary>
    public HttpClient CreateClient() => Api.CreateClient();

    /// <summary>Builds a second host against the same containers, with a clock the caller controls.</summary>
    public ApiFactory CreateHostWithClock(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return new ApiFactory(
            SettingsFor(IdentityConnectionString, _redis.GetConnectionString()),
            services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            });
    }

    /// <summary>Builds a host pointed at dependencies that cannot possibly answer.</summary>
    public static ApiFactory CreateHostWithUnreachableDependencies() =>
        new(SettingsFor(
            "Host=127.0.0.1;Port=1;Database=nowhere;Username=nobody;Password=nothing;Timeout=2;"
            + "Command Timeout=2;Maximum Pool Size=2",
            "127.0.0.1:1,abortConnect=false,connectTimeout=500,connectRetry=1"));

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        await ApplyMigrationsAsync();

        _api = new ApiFactory(
            SettingsFor(IdentityConnectionString, _redis.GetConnectionString()),
            services => ModuleDbContextInterceptors.AddToIdentity(services, RecordedCommands));

        // Force the host to build here rather than inside the first test: a configuration mistake then fails.
        _ = _api.Services;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private static Dictionary<string, string?> SettingsFor(string identity, string redis) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Identity"] = identity,
            ["ConnectionStrings:Redis"] = redis,
            ["Jwt:SigningKey"] = SigningKey,
            ["Jwt:Issuer"] = "StockPortfolio",
            ["Jwt:Audience"] = "StockPortfolio",
            ["Cors:Origins:0"] = CorsOrigin,
        };

    private string ConnectionStringFor(string role, string password) => ConnectionStringFor(
        role,
        password,
        _postgres.Hostname,
        _postgres.GetMappedPublicPort(PostgreSqlBuilder.PostgreSqlPort));

    private static string ConnectionStringFor(string role, string password, string host, int port) =>
        string.Create(
            CultureInfo.InvariantCulture,
            // Maximum Pool Size is 10 rather than production's 2.
            $"Host={host};Port={port};Database={DatabaseName};Username={role};Password={password};"
            + $"Maximum Pool Size=10;Include Error Detail=true");

    /// <summary>Applies the Identity migration as migrator.</summary>
    private async Task ApplyMigrationsAsync()
    {
        await using var migratorHost = new ApiFactory(
            SettingsFor(MigratorConnectionString, _redis.GetConnectionString()));

        using var scope = migratorHost.Services.CreateScope();

        var context = (DbContext)scope.ServiceProvider.GetRequiredService(
            ModuleDbContextInterceptors.IdentityDbContextType());

        await context.Database.MigrateAsync();
    }

    /// <summary>Fails fast when the mounted shell script has CRLF line endings.</summary>
    private static void GuardAgainstWindowsLineEndings()
    {
        var script = File.ReadAllText(RepositoryPaths.RolesShellScript);

        if (script.Contains("\r\n", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{RepositoryPaths.RolesShellScript}' has CRLF line endings. The Postgres container "
                + "executes it with bash, which fails on '#!/bin/bash\\r', aborting database "
                + "initialisation. .gitattributes pins *.sh to eol=lf; re-check out the file "
                + "(git rm --cached -r . && git reset --hard) to restore it.");
        }
    }
}
