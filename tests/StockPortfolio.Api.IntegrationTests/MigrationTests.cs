using Npgsql;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>
/// What the migration and <c>db/init/01-roles.sql</c> left behind, asserted against the live server.
/// </summary>
/// <param name="fixture">The shared containers and host.</param>
[Collection(ApiCollectionDefinition.Name)]
public sealed class MigrationTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>
    /// The four module schemas exist, <c>identity</c> holds both tables, and — the part that is easy
    /// to get wrong — the migrations history table sits in <c>identity</c> rather than <c>public</c>.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// <c>HasDefaultSchema</c> moves the entity tables but <b>not</b> <c>__EFMigrationsHistory</c>
    /// (efcore#24127, closed <i>not planned</i>). Only
    /// <c>MigrationsHistoryTable("__EFMigrationsHistory", "identity")</c> on the Npgsql options builder
    /// does that. Get it wrong and all four module contexts share
    /// <c>public.__EFMigrationsHistory</c>, each sees the others' migration ids, and
    /// <c>database update</c> reports migrations as applied-but-missing — a failure that reads as data
    /// corruption and arrives three phases from now. Hence the explicit
    /// "and it is not in public" assertion: finding it in <c>identity</c> is not enough on its own if a
    /// stray copy also exists.
    /// </remarks>
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

        // The migration is recorded, not merely the tables created: an empty history table with the
        // right shape would mean Migrate() had been bypassed by EnsureCreated somewhere.
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
