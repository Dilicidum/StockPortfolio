using Npgsql;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class SchemaIsolationTests(ApiFixture fixture)
{
    // The SQLSTATE for insufficient_privilege.
    private const string InsufficientPrivilege = "42501";

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public async Task PortfolioRole_CannotReadIdentitySchema()
    {
        await using var connection = new NpgsqlConnection(_fixture.PortfolioConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("SELECT id, email FROM identity.users", connection);

        var thrown = await Should.ThrowAsync<PostgresException>(
            async () => await command.ExecuteReaderAsync(TestContext.Current.CancellationToken));

        thrown.SqlState.ShouldBe(InsufficientPrivilege);
    }

    [Fact]
    public async Task PortfolioRole_HasNoUsageOnIdentitySchema()
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT has_schema_privilege(@role, 'identity',  'USAGE'),
                   has_schema_privilege(@role, 'portfolio', 'USAGE')
            """,
            connection);

        command.Parameters.AddWithValue("role", ApiFixture.PortfolioRole);

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        reader.GetBoolean(0).ShouldBeFalse("portfolio_svc must not have USAGE on the identity schema");
        reader.GetBoolean(1).ShouldBeTrue("portfolio_svc must have USAGE on its own schema");
    }

    [Fact]
    public async Task IdentityRole_CanReadItsOwnSchema()
    {
        await using var connection = new NpgsqlConnection(_fixture.IdentityConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        // Quoted because the library's table names are PascalCase, unlike this repo's own snake_case ones.
        await using var command = new NpgsqlCommand("""SELECT count(*) FROM identity."AspNetUsers" """, connection);

        var count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        count.ShouldBeOfType<long>().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task AlertsRole_HasUsageOnAlertsAlone()
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT has_schema_privilege(@role, 'alerts',     'USAGE'),
                   has_schema_privilege(@role, 'identity',   'USAGE'),
                   has_schema_privilege(@role, 'portfolio',  'USAGE'),
                   has_schema_privilege(@role, 'marketdata', 'USAGE')
            """,
            connection);

        command.Parameters.AddWithValue("role", ApiFixture.AlertsRole);

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        reader.GetBoolean(0).ShouldBeTrue("alerts_svc must have USAGE on its own schema");
        reader.GetBoolean(1).ShouldBeFalse("alerts_svc must not have USAGE on the identity schema");
        reader.GetBoolean(2).ShouldBeFalse("alerts_svc must not have USAGE on the portfolio schema");
        reader.GetBoolean(3).ShouldBeFalse("alerts_svc must not have USAGE on the marketdata schema");
    }

    [Fact]
    public async Task AlertsRole_CanReadItsOwnTables()
    {
        await using var connection = new NpgsqlConnection(_fixture.AlertsConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM alerts.alert_settings",
            connection);

        var count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        count.ShouldBeOfType<long>().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task MarketDataRole_HasUsageOnMarketDataAlone()
    {
        await using var connection = new NpgsqlConnection(_fixture.MigratorConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT has_schema_privilege(@role, 'marketdata', 'USAGE'),
                   has_schema_privilege(@role, 'identity',   'USAGE'),
                   has_schema_privilege(@role, 'portfolio',  'USAGE'),
                   has_schema_privilege(@role, 'alerts',     'USAGE')
            """,
            connection);

        command.Parameters.AddWithValue("role", ApiFixture.MarketDataRole);

        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        (await reader.ReadAsync(TestContext.Current.CancellationToken)).ShouldBeTrue();

        reader.GetBoolean(0).ShouldBeTrue("marketdata_svc must have USAGE on its own schema");
        reader.GetBoolean(1).ShouldBeFalse("marketdata_svc must not have USAGE on the identity schema");
        reader.GetBoolean(2).ShouldBeFalse("marketdata_svc must not have USAGE on the portfolio schema");
        reader.GetBoolean(3).ShouldBeFalse("marketdata_svc must not have USAGE on the alerts schema");
    }

    [Fact]
    public async Task MarketDataRole_CanReadItsOwnTables()
    {
        await using var connection = new NpgsqlConnection(_fixture.MarketDataConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM marketdata.user_provider_keys",
            connection);

        var count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        count.ShouldBeOfType<long>().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task IdentityRole_CannotRunDdl()
    {
        await using var connection = new NpgsqlConnection(_fixture.IdentityConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(
            "CREATE TABLE identity.should_not_exist (id uuid PRIMARY KEY)",
            connection);

        var thrown = await Should.ThrowAsync<PostgresException>(
            async () => await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));

        thrown.SqlState.ShouldBe(InsufficientPrivilege);
    }
}
