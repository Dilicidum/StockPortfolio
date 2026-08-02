using Npgsql;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>
/// The database-level module boundary: one role per module, no cross-schema reads.
/// </summary>
/// <param name="fixture">The shared containers and host.</param>
/// <remarks>
/// This is the executable form of the claim <c>db/init/01-roles.sql</c> makes. The architecture tests
/// stop <c>Portfolio.Application</c> from <i>compiling</i> against <c>Identity.Domain</c>; this stops
/// <c>portfolio_svc</c> from reading <c>identity.users</c> even if someone writes the query anyway.
/// </remarks>
[Collection(ApiCollectionDefinition.Name)]
public sealed class SchemaIsolationTests(ApiFixture fixture)
{
    /// <summary>
    /// <c>insufficient_privilege</c>. Asserted as a code, never as a message: Postgres localises
    /// <c>errmsg</c> according to <c>lc_messages</c>, so a server running under a non-English locale
    /// would fail a message-matching test while behaving identically.
    /// </summary>
    private const string InsufficientPrivilege = "42501";

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>The Portfolio role cannot read the Identity module's tables.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
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

    /// <summary>The Portfolio role cannot reach the Identity schema at all, not merely its tables.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// <c>REVOKE ALL ON SCHEMA identity FROM PUBLIC</c> plus no <c>GRANT USAGE</c> is what produces the
    /// failure above. Asserting the privilege directly says <i>why</i> the query failed, so a future
    /// <c>GRANT</c> that re-opens the schema fails here with a readable diagnosis rather than only as a
    /// missing exception one test up.
    /// </remarks>
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

    /// <summary>The Identity role can read its own tables, so the test above is not vacuous.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// A grants file that revoked everything from everyone would pass
    /// <see cref="PortfolioRole_CannotReadIdentitySchema"/> perfectly and break the application. The
    /// isolation claim is only meaningful next to the access claim.
    /// </remarks>
    [Fact]
    public async Task IdentityRole_CanReadItsOwnSchema()
    {
        await using var connection = new NpgsqlConnection(_fixture.IdentityConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand("SELECT count(*) FROM identity.users", connection);

        var count = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        count.ShouldBeOfType<long>().ShouldBeGreaterThanOrEqualTo(0);
    }

    /// <summary>The Identity role holds DML only — it cannot create tables.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is the reason the fixture migrates as <c>migrator</c>. If a service role could run DDL, the
    /// separation would be decorative and an application bug could reshape the schema.
    /// </remarks>
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
