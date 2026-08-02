using Npgsql;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>What the migration and db/init/01-roles.sql left behind, asserted against the live server.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class MigrationTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>The four module schemas exist, identity holds both tables, and — the part that is easy to get wrong.</summary>
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

        var historySchemas = await ReadStringsAsync(
            connection,
            """
            SELECT table_schema FROM information_schema.tables
             WHERE table_name = '__EFMigrationsHistory'
             ORDER BY table_schema
            """);

        historySchemas.ShouldBe(["identity"]);
        historySchemas.ShouldNotContain("public");

        // The migration is recorded, not merely the tables created: an empty history table with the right.
        var applied = await ReadStringsAsync(
            connection,
            """SELECT "MigrationId" FROM identity."__EFMigrationsHistory" ORDER BY "MigrationId" """);

        applied.ShouldNotBeEmpty();
        applied.ShouldContain(id => id.EndsWith("InitialIdentity", StringComparison.Ordinal));
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
