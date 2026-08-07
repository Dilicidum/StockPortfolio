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

public sealed class ApiFixture : IAsyncLifetime
{
    // Matches docker-compose.yml, so the tests exercise the server version that ships.
    private const string PostgresImage = "postgres:18-alpine";

    private const string RedisImage = "redis:8-alpine";

    private const string DatabaseName = "stockportfolio";
    private const string SuperUserName = "postgres";
    private const string SuperUserPassword = "postgres_test_only";

    public const string MigratorRole = "migrator";

    public const string IdentityRole = "identity_svc";

    public const string PortfolioRole = "portfolio_svc";

    public const string AlertsRole = "alerts_svc";

    public const string MarketDataRole = "marketdata_svc";

    private const string RolePassword = "role_test_only";

    private const string SigningKey = "integration-test-signing-key-not-a-secret-0123";

    public const string FakeProviderName = "Fake";

    private const string CorsOrigin = "http://localhost:5173";

    private readonly PostgreSqlContainer _postgres;
    private readonly RedisContainer _redis;

    private ApiFactory? _api;

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

    public RecordingDbCommandInterceptor RecordedCommands { get; } = new();

    public IServiceProvider Services => Api.Services;

    public string MigratorConnectionString => ConnectionStringFor(MigratorRole, RolePassword);

    public string IdentityConnectionString => ConnectionStringFor(IdentityRole, RolePassword);

    public string PortfolioConnectionString => ConnectionStringFor(PortfolioRole, RolePassword);

    public string AlertsConnectionString => ConnectionStringFor(AlertsRole, RolePassword);

    public string MarketDataConnectionString => ConnectionStringFor(MarketDataRole, RolePassword);

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

    public string RedisConnectionString => _redis.GetConnectionString();

    public IReadOnlyList<Type> HostDbContextTypes { get; private set; } = [];

    private ApiFactory Api => _api ?? throw new InvalidOperationException("The fixture has not been initialised.");

    public HttpClient CreateClient() => Api.CreateClient();

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

                // RemoveAll<T> matches the service type only, so without this line IQuoteNudge still resolves the fake.
                services.RemoveAll<IQuoteNudge>();

                services.AddSingleton(provider);
            });
    }

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

    public ApiFactory CreateHostWithRedisDown() => new(SettingsFor(new ModuleConnectionStrings(
        IdentityConnectionString,
        PortfolioConnectionString,
        AlertsConnectionString,
        MarketDataConnectionString,

        // A dead port, per host and reversible: stopping the shared container would mutate fixture state other classes rely on.
        "127.0.0.1:1,abortConnect=false,connectTimeout=500,connectRetry=1")));

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
                // All four, or ParameterisationTests' assembly-wide proof silently stops covering the modules left out.
                ModuleDbContextInterceptors.AddToIdentity(services, RecordedCommands);
                ModuleDbContextInterceptors.AddToPortfolio(services, RecordedCommands);
                ModuleDbContextInterceptors.AddToAlerts(services, RecordedCommands);
                ModuleDbContextInterceptors.AddToMarketData(services, RecordedCommands);

                // ConfigureTestServices runs after the app's own registrations, so this is the whole collection — comparable with the Migrator's.
                HostDbContextTypes = MigratedModules.DbContextTypesIn(services);
            });

        // Force the host to build here rather than inside the first test: a configuration mistake then fails.
        _ = _api.Services;

        GuardAgainstTheLiveQuoteProvider(_api.Services);
    }

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

            // Explicitly empty, appended last: it beats an exported Finnhub__ApiKey that would boot the suite onto the live API and still report green.
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

    private async Task ApplyMigrationsAsync()
    {
        // A bare ServiceCollection as the Migrator builds one: a module whose Add<M>Module leans on the host stops migrating here too.
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

    private static void GuardAgainstTheLiveQuoteProvider(IServiceProvider services)
    {
        // The name, not the type: FakeQuoteProvider is internal to MarketData.Infrastructure.
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

internal sealed record ModuleConnectionStrings(
    string Identity, string Portfolio, string Alerts, string MarketData, string Redis);
