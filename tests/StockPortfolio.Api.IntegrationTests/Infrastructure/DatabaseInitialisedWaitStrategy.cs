using System.Net.Sockets;

using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

using Npgsql;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Waits until <c>docker-entrypoint-initdb.d</c> has finished, not merely until Postgres accepts a
/// connection.
/// </summary>
/// <param name="connectionStringFactory">Builds a superuser connection string for the running container.</param>
/// <remarks>
/// <para>
/// <b>This class exists because of a genuinely nasty trap.</b> Testcontainers' default Postgres wait
/// strategy runs <c>pg_isready</c> <i>inside</i> the container. During initialisation the entrypoint
/// starts a temporary server, runs every script in <c>docker-entrypoint-initdb.d</c> against it, then
/// stops it and starts the real one — so <c>pg_isready</c> answers "ready" while
/// <c>01-roles.sql</c> has not run yet. A test that then connects as <c>portfolio_svc</c> fails with
/// "role does not exist", and because the race is timing-dependent it looks like flakiness rather
/// than a missing wait.
/// </para>
/// <para>
/// The probe below is deliberately made <i>from the host, over TCP</i>. The temporary server is
/// started with <c>listen_addresses=''</c> and therefore accepts nothing but the Unix socket, so any
/// successful TCP connection is proof that initialisation finished and the real server took over.
/// Counting the five login roles on top of that turns the proof into an assertion about the thing we
/// actually depend on, and <c>has_schema_privilege</c> confirms the grants at the far end of the
/// script ran too — checking only that the roles exist would go green halfway through the file.
/// </para>
/// </remarks>
internal sealed class DatabaseInitialisedWaitStrategy(Func<IContainer, string> connectionStringFactory) : IWaitUntil
{
    /// <summary>
    /// Every role <c>01-roles.sql</c> creates, plus a grant from the far end of the same file.
    /// </summary>
    /// <remarks>
    /// <c>CASE</c> short-circuits, which matters: <c>has_schema_privilege</c> raises
    /// <c>22023 invalid_parameter_value</c> for a role that does not exist, and an exception in a
    /// wait strategy would be indistinguishable from "not ready yet".
    /// </remarks>
    private const string ProbeSql = """
        SELECT CASE
            WHEN (SELECT count(*) FROM pg_roles
                   WHERE rolname IN ('migrator', 'identity_svc', 'portfolio_svc', 'marketdata_svc', 'alerts_svc')) = 5
            THEN has_schema_privilege('identity_svc', 'identity', 'USAGE')
            ELSE false
        END
        """;

    /// <inheritdoc/>
    public async Task<bool> UntilAsync(IContainer container)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionStringFactory(container));
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(ProbeSql, connection);

            return await command.ExecuteScalarAsync() is true;
        }
        catch (NpgsqlException)
        {
            // Refused, reset, or the database is still starting. Both are "not yet", not "broken".
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
