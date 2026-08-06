using Npgsql;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>What the migration and db/init/01-roles.sql left behind, asserted against the live server.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class MigrationTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>The four module schemas exist, every module holds its tables, and — the part that is easy to get wrong.</summary>
    [Fact]
    public async Task Migrations_ApplyCleanly_OnEmptyDatabase()
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        var schemas = await ReadStringsAsync(
            connection,
            """
            SELECT nspname FROM pg_namespace
             WHERE nspname IN ('identity', 'portfolio', 'marketdata', 'alerts')
             ORDER BY nspname
            """);

        schemas.ShouldBe(["alerts", "identity", "marketdata", "portfolio"]);

        var identityTables = await ReadStringsAsync(
            connection,
            """
            SELECT table_name FROM information_schema.tables
             WHERE table_schema = 'identity'
             ORDER BY table_name
            """);

        identityTables.ShouldContain("users");
        identityTables.ShouldContain("refresh_tokens");
        identityTables.ShouldContain("__EFMigrationsHistory");

        var portfolioTables = await ReadStringsAsync(
            connection,
            """
            SELECT table_name FROM information_schema.tables
             WHERE table_schema = 'portfolio'
             ORDER BY table_name
            """);

        portfolioTables.ShouldContain("holdings");
        portfolioTables.ShouldContain("__EFMigrationsHistory");

        var alertsTables = await ReadStringsAsync(
            connection,
            """
            SELECT table_name FROM information_schema.tables
             WHERE table_schema = 'alerts'
             ORDER BY table_name
            """);

        alertsTables.ShouldContain("alert_settings");
        alertsTables.ShouldContain("fired_alerts");
        alertsTables.ShouldContain("__EFMigrationsHistory");

        var marketDataTables = await ReadStringsAsync(
            connection,
            """
            SELECT table_name FROM information_schema.tables
             WHERE table_schema = 'marketdata'
             ORDER BY table_name
            """);

        marketDataTables.ShouldContain("user_provider_keys");
        marketDataTables.ShouldContain("data_protection_keys");
        marketDataTables.ShouldContain("__EFMigrationsHistory");

        var historySchemas = await ReadStringsAsync(
            connection,
            """
            SELECT table_schema FROM information_schema.tables
             WHERE table_name = '__EFMigrationsHistory'
             ORDER BY table_schema
            """);

        // The load-bearing line, and with a fourth context it finally has something to say: HasDefaultSchema
        // does not move __EFMigrationsHistory, so without MigrationsHistoryTable per context all four land
        // in public, each context reads the others' ids as applied, and it looks exactly like corruption.
        historySchemas.ShouldBe(["alerts", "identity", "marketdata", "portfolio"]);
        historySchemas.ShouldNotContain("public");

        // The migration is recorded, not merely the tables created: an empty history table with the right.
        var applied = await ReadStringsAsync(
            connection,
            """SELECT "MigrationId" FROM identity."__EFMigrationsHistory" ORDER BY "MigrationId" """);

        applied.ShouldNotBeEmpty();
        applied.ShouldContain(id => id.EndsWith("InitialIdentity", StringComparison.Ordinal));

        var portfolioApplied = await ReadStringsAsync(
            connection,
            """SELECT "MigrationId" FROM portfolio."__EFMigrationsHistory" ORDER BY "MigrationId" """);

        portfolioApplied.ShouldNotBeEmpty();
        portfolioApplied.ShouldContain(id => id.EndsWith("InitialPortfolio", StringComparison.Ordinal));

        var alertsApplied = await ReadStringsAsync(
            connection,
            """SELECT "MigrationId" FROM alerts."__EFMigrationsHistory" ORDER BY "MigrationId" """);

        alertsApplied.ShouldNotBeEmpty();
        alertsApplied.ShouldContain(id => id.EndsWith("InitialAlerts", StringComparison.Ordinal));

        var marketDataApplied = await ReadStringsAsync(
            connection,
            """SELECT "MigrationId" FROM marketdata."__EFMigrationsHistory" ORDER BY "MigrationId" """);

        marketDataApplied.ShouldNotBeEmpty();
        marketDataApplied.ShouldContain(id => id.EndsWith("InitialMarketData", StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        var values = new List<string>();

        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }
}
