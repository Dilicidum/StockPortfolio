using System.Globalization;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The one Postgres container, the one Redis container and the one API host that every integration
/// test in this assembly shares.
/// </summary>
/// <remarks>
/// <para>
/// <b>One container per run, not per class.</b> This is an <c>ICollectionFixture</c> behind a single
/// <c>[CollectionDefinition]</c>, so xUnit constructs it once for the whole assembly and runs every
/// test in the collection sequentially against it. A per-class fixture would pay ~5 seconds of
/// container startup per test class and give the tests a fresh database each time, which sounds
/// tidier and is not: the schema, the roles and the grants are the shared state under test.
/// </para>
/// <para>
/// <b>The database is initialised by the files that ship.</b> <c>db/init/00-roles.sh</c> is mounted
/// into <c>/docker-entrypoint-initdb.d</c> and <c>db/init</c> is mounted separately at
/// <c>/db/init</c> — the same two mounts, in the same shape, as <c>docker-compose.yml</c>. The
/// separation is not cosmetic: the entrypoint globs <c>/docker-entrypoint-initdb.d</c> and runs
/// <i>everything</i> in it, so a <c>01-roles.sql</c> visible there would be executed a second time
/// with no <c>-v</c> variables, and <c>:'migrator_pw'</c> is then a syntax error that aborts
/// initialisation. Copying the SQL into this project instead would test a copy; mounting the
/// original means <c>PortfolioRole_CannotReadIdentitySchema</c> asserts against the security model
/// that actually deploys.
/// </para>
/// <para>
/// <b>Migrations run as <c>migrator</c>, not as a service role.</b> The service roles hold DML only —
/// that is the whole point of the split — so applying the migration as <c>identity_svc</c> would fail
/// with <c>42501</c> on the first <c>CREATE TABLE</c>. A throwaway host is built with the migrator
/// connection string purely to reach the module's <c>DbContext</c>, which is <see langword="internal"/>
/// and therefore resolvable only by type object.
/// </para>
/// </remarks>
public sealed class ApiFixture : IAsyncLifetime
{
    /// <summary>Matches <c>docker-compose.yml</c>, so the tests exercise the same server version that ships.</summary>
    private const string PostgresImage = "postgres:18-alpine";

    /// <inheritdoc cref="PostgresImage"/>
    private const string RedisImage = "redis:8-alpine";

    private const string DatabaseName = "stockportfolio";
    private const string SuperUserName = "postgres";
    private const string SuperUserPassword = "postgres_test_only";

    /// <summary>The DDL role. Owns all four schemas; the only role allowed to migrate.</summary>
    public const string MigratorRole = "migrator";

    /// <summary>The Identity module's DML role.</summary>
    public const string IdentityRole = "identity_svc";

    /// <summary>The Portfolio module's DML role. Used to prove it cannot read <c>identity</c>.</summary>
    public const string PortfolioRole = "portfolio_svc";

    private const string RolePassword = "role_test_only";

    /// <summary>
    /// A 46-byte key. <c>AddStockPortfolioAuthentication</c> rejects anything under 32 UTF-8 bytes,
    /// because HMAC-SHA256 does.
    /// </summary>
    private const string SigningKey = "integration-test-signing-key-not-a-secret-0123";

    private const string CorsOrigin = "http://localhost:5173";

    private readonly PostgreSqlContainer _postgres;
    private readonly RedisContainer _redis;

    private ApiFactory? _api;

    /// <summary>Creates the fixture. Containers are started by <see cref="InitializeAsync"/>.</summary>
    public ApiFixture()
    {
        GuardAgainstWindowsLineEndings();

        _postgres = new PostgreSqlBuilder(PostgresImage)
            .WithUsername(SuperUserName)
            .WithPassword(SuperUserPassword)
            .WithDatabase(DatabaseName)

            // postgres:18 moved its default data directory under /var/lib/postgresql/<major>/. Pinning
            // PGDATA the way compose does keeps the two environments byte-identical.
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

    /// <summary>Gets every SQL statement the shared host has executed. See <c>ParameterisationTests</c>.</summary>
    public RecordingDbCommandInterceptor RecordedCommands { get; } = new();

    /// <summary>Gets the shared host's service provider.</summary>
    public IServiceProvider Services => Api.Services;

    /// <summary>Gets the connection string for the migrator role, which owns the four schemas.</summary>
    public string MigratorConnectionString => ConnectionStringFor(MigratorRole, RolePassword);

    /// <summary>Gets the connection string the API itself uses.</summary>
    public string IdentityConnectionString => ConnectionStringFor(IdentityRole, RolePassword);

    /// <summary>Gets the connection string for the Portfolio role, which must not see <c>identity</c>.</summary>
    public string PortfolioConnectionString => ConnectionStringFor(PortfolioRole, RolePassword);

    private ApiFactory Api => _api ?? throw new InvalidOperationException("The fixture has not been initialised.");

    /// <summary>Creates a client against the shared host.</summary>
    /// <returns>A client whose base address is the in-memory test server.</returns>
    public HttpClient CreateClient() => Api.CreateClient();

    /// <summary>
    /// Builds a second host against the same containers, with a clock the caller controls.
    /// </summary>
    /// <param name="clock">The clock to inject in place of <see cref="TimeProvider.System"/>.</param>
    /// <returns>A host the caller owns and must dispose.</returns>
    /// <remarks>
    /// A separate host rather than a mutable clock on the shared one: tests in a collection run in
    /// order, but they must not <i>depend</i> on order, and a shared clock that one test winds forward
    /// is exactly that dependency.
    /// </remarks>
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

    /// <summary>
    /// Builds a host pointed at dependencies that cannot possibly answer.
    /// </summary>
    /// <returns>A host the caller owns and must dispose.</returns>
    /// <remarks>
    /// Port 1 is reserved and nothing listens on it, so Postgres and Redis are unreachable while the
    /// process itself is perfectly healthy — which is precisely the situation the liveness/readiness
    /// split exists to tell apart.
    /// </remarks>
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

        // Force the host to build here rather than inside the first test: a configuration mistake then
        // fails the fixture with its own message instead of surfacing as an unrelated test failure.
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
            // Maximum Pool Size is 10 rather than production's 2. The cap exists because Azure B1ms
            // allows 35 connections across four roles and two replicas; a laptop running one host has
            // no such ceiling, and a pool of 2 shared with the readiness check's own connection turns
            // an ordinary test into a pool-exhaustion timeout.
            $"Host={host};Port={port};Database={DatabaseName};Username={role};Password={password};"
            + $"Maximum Pool Size=10;Include Error Detail=true");

    /// <summary>
    /// Applies the Identity migration as <c>migrator</c>.
    /// </summary>
    /// <remarks>
    /// The context is <see langword="internal"/>, so it is resolved by <see cref="Type"/> and used
    /// through the public <see cref="DbContext"/> base — the same trick, and the same reason, as
    /// <see cref="ModuleDbContextInterceptors"/>.
    /// </remarks>
    private async Task ApplyMigrationsAsync()
    {
        await using var migratorHost = new ApiFactory(
            SettingsFor(MigratorConnectionString, _redis.GetConnectionString()));

        using var scope = migratorHost.Services.CreateScope();

        var context = (DbContext)scope.ServiceProvider.GetRequiredService(
            ModuleDbContextInterceptors.IdentityDbContextType());

        await context.Database.MigrateAsync();
    }

    /// <summary>
    /// Fails fast when the mounted shell script has CRLF line endings.
    /// </summary>
    /// <remarks>
    /// <c>.gitattributes</c> forces <c>*.sh</c> to <c>eol=lf</c> precisely because a Windows clone with
    /// <c>core.autocrlf=true</c> would otherwise give the container <c>#!/bin/bash\r</c>, Postgres
    /// initialisation would abort, and <c>docker compose up</c> would fail from a clean clone — P0
    /// req 7. Without this guard that shows up as an inscrutable container timeout; with it, the
    /// fixture names the cause.
    /// </remarks>
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
