using System.Globalization;

using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using StockPortfolio.Migrator;
using StockPortfolio.Modules.MarketData.Application.Abstractions;

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

    /// <summary>The Alerts module's DML role — the one E1 was waiting for a consumer of.</summary>
    public const string AlertsRole = "alerts_svc";

    /// <summary>The MarketData module's DML role — unused until this module owned a table.</summary>
    public const string MarketDataRole = "marketdata_svc";

    private const string RolePassword = "role_test_only";

    /// <summary>A 46-byte key.</summary>
    private const string SigningKey = "integration-test-signing-key-not-a-secret-0123";

    /// <summary>What IQuoteProvider.Name must read on every host these tests build.</summary>
    public const string FakeProviderName = "Fake";

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

    /// <summary>Gets the connection string for the Alerts role, which must not see identity either.</summary>
    public string AlertsConnectionString => ConnectionStringFor(AlertsRole, RolePassword);

    /// <summary>Gets the connection string for the MarketData role, which must not see any other schema.</summary>
    public string MarketDataConnectionString => ConnectionStringFor(MarketDataRole, RolePassword);

    /// <summary>Gets the configuration the module list is registered against, shaped like the Migrator's own.</summary>
    public IConfiguration MigratorConfiguration => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            // Every module on the migrator role: it owns every schema, and no _svc role has DDL rights.
            ["ConnectionStrings:Identity"] = MigratorConnectionString,
            ["ConnectionStrings:Portfolio"] = MigratorConnectionString,
            ["ConnectionStrings:Alerts"] = MigratorConnectionString,
            ["ConnectionStrings:MarketData"] = MigratorConnectionString,
        })
        .Build();

    /// <summary>Gets the Redis endpoint, so a test can open a SECOND connection and prove the fan-out.</summary>
    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>Gets the module DbContext types the API host registered, read off the host as it was built.</summary>
    public IReadOnlyList<Type> HostDbContextTypes { get; private set; } = [];

    private ApiFactory Api => _api ?? throw new InvalidOperationException("The fixture has not been initialised.");

    /// <summary>Creates a client against the shared host.</summary>
    public HttpClient CreateClient() => Api.CreateClient();

    /// <summary>Builds a second host against the same containers, with a clock the caller controls.</summary>
    public ApiFactory CreateHostWithClock(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return new ApiFactory(
            SettingsFor(new ModuleConnectionStrings(
                IdentityConnectionString,
                PortfolioConnectionString,
                AlertsConnectionString,
                MarketDataConnectionString,
                _redis.GetConnectionString())),
            services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(clock);
            });
    }

    /// <summary>Builds a second host against the same containers, serving prices from the caller's provider.</summary>
    public ApiFactory CreateHostWithQuoteProvider(IQuoteProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return new ApiFactory(
            SettingsFor(new ModuleConnectionStrings(
                IdentityConnectionString,
                PortfolioConnectionString,
                AlertsConnectionString,
                MarketDataConnectionString,
                _redis.GetConnectionString())),
            services =>
            {
                services.RemoveAll<IQuoteProvider>();

                // RemoveAll<T> removes descriptors whose ServiceType is T and nothing else, so without
                // this line IQuoteNudge still resolves the fake and the two seams disagree about which
                // provider is live. The nudge route is unmapped under EnvironmentName "Testing", so
                // Dashboard_HostWithQuoteProvider_ResolvesNoQuoteNudge is what makes deleting this fail.
                services.RemoveAll<IQuoteNudge>();

                services.AddSingleton(provider);
            });
    }

    /// <summary>Builds a second host with the BYOK feature switched off, so 404 can be driven for real.</summary>
    public ApiFactory CreateHostWithByokDisabled()
    {
        var settings = SettingsFor(new ModuleConnectionStrings(
            IdentityConnectionString,
            PortfolioConnectionString,
            AlertsConnectionString,
            MarketDataConnectionString,
            _redis.GetConnectionString()));

        settings["MarketData:Byok:Enabled"] = "false";

        return new ApiFactory(settings);
    }

    /// <summary>Builds a host whose Redis cannot answer while its quote provider still can.</summary>
    public ApiFactory CreateHostWithRedisDown() => new(SettingsFor(new ModuleConnectionStrings(
        IdentityConnectionString,
        PortfolioConnectionString,
        AlertsConnectionString,
        MarketDataConnectionString,

        // A port nothing listens on, per host and reversible. Stopping the _redis container instead would
        // mutate shared fixture state with no guarantee this class runs last. abortConnect=false stops
        // Connect throwing at first resolve; the bounded timeout is because the default 5000 ms would
        // otherwise be paid on every call.
        "127.0.0.1:1,abortConnect=false,connectTimeout=500,connectRetry=1")));

    /// <summary>Builds a host pointed at dependencies that cannot possibly answer.</summary>
    public static ApiFactory CreateHostWithUnreachableDependencies()
    {
        const string Nowhere = "Host=127.0.0.1;Port=1;Database=nowhere;Username=nobody;Password=nothing;"
            + "Timeout=2;Command Timeout=2;Maximum Pool Size=2";

        return new ApiFactory(SettingsFor(new ModuleConnectionStrings(
            Nowhere,
            Nowhere,
            Nowhere,
            Nowhere,
            "127.0.0.1:1,abortConnect=false,connectTimeout=500,connectRetry=1")));
    }

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        await ApplyMigrationsAsync();

        _api = new ApiFactory(
            SettingsFor(new ModuleConnectionStrings(
                IdentityConnectionString,
                PortfolioConnectionString,
                AlertsConnectionString,
                MarketDataConnectionString,
                _redis.GetConnectionString())),
            services =>
            {
                // Both, or ParameterisationTests' assembly-wide proof silently stops covering holdings.
                ModuleDbContextInterceptors.AddToIdentity(services, RecordedCommands);
                ModuleDbContextInterceptors.AddToPortfolio(services, RecordedCommands);
                ModuleDbContextInterceptors.AddToAlerts(services, RecordedCommands);
                ModuleDbContextInterceptors.AddToMarketData(services, RecordedCommands);

                // ConfigureTestServices runs after the app's own registrations, so this is the whole
                // collection - which is what makes it comparable with the Migrator's.
                HostDbContextTypes = MigratedModules.DbContextTypesIn(services);
            });

        // Force the host to build here rather than inside the first test: a configuration mistake then fails.
        _ = _api.Services;

        GuardAgainstTheLiveQuoteProvider(_api.Services);
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

    private static Dictionary<string, string?> SettingsFor(ModuleConnectionStrings connections) =>
        new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ConnectionStrings:Identity"] = connections.Identity,
            ["ConnectionStrings:Portfolio"] = connections.Portfolio,
            ["ConnectionStrings:Alerts"] = connections.Alerts,
            ["ConnectionStrings:MarketData"] = connections.MarketData,
            ["ConnectionStrings:Redis"] = connections.Redis,
            ["Jwt:SigningKey"] = SigningKey,
            ["Jwt:Issuer"] = "StockPortfolio",
            ["Jwt:Audience"] = "StockPortfolio",
            ["Cors:Origins:0"] = CorsOrigin,

            // Explicitly empty, never merely omitted. ApiFactory appends this collection AFTER the
            // default sources, so it beats an exported Finnhub__ApiKey - which would otherwise boot the
            // test host onto the live API and make rate-limited network calls while the suite stayed green.
            ["Finnhub:ApiKey"] = string.Empty,
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

    /// <summary>Applies every module's migrations through the Migrator's own module list, not a copy of it.</summary>
    private async Task ApplyMigrationsAsync()
    {
        // A bare ServiceCollection, exactly as Migrator/Program.cs builds one: no host, no ASP.NET Core,
        // so a module whose Add<M>Module leans on the host silently stops migrating here too.
        var services = new ServiceCollection();

        services.AddEveryMigratedModule(MigratorConfiguration);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        foreach (var contextType in MigratedModules.DbContextTypesIn(services))
        {
            var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);

            await context.Database.MigrateAsync();
        }
    }

    /// <summary>Fails the whole run if the host booted onto anything but the generated provider.</summary>
    private static void GuardAgainstTheLiveQuoteProvider(IServiceProvider services)
    {
        // The name rather than the type: FakeQuoteProvider is internal to MarketData.Infrastructure, and
        // IQuoteProvider.Name is the same string the startup log and the health route already publish.
        var provider = services.GetRequiredService<IQuoteProvider>().Name;

        if (!string.Equals(provider, FakeProviderName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The test host is serving prices from '{provider}', not '{FakeProviderName}'. "
                + "SettingsFor pins Finnhub:ApiKey to empty precisely so an exported Finnhub__ApiKey "
                + "cannot do this; the whole suite would otherwise make rate-limited calls to the live "
                + "API and still report green.");
        }
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

/// <summary>One host's worth of connection strings. A fifth positional SettingsFor argument was the last one; the next module needs a sixth.</summary>
internal sealed record ModuleConnectionStrings(
    string Identity, string Portfolio, string Alerts, string MarketData, string Redis);
