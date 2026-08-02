using System.Net.Sockets;

using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

using Npgsql;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>Waits until docker-entrypoint-initdb.d has finished, not merely until Postgres accepts a connection.</summary>
internal sealed class DatabaseInitialisedWaitStrategy(Func<IContainer, string> connectionStringFactory) : IWaitUntil
{
    /// <summary>Every role 01-roles.sql creates, plus a grant from the far end of the same file.</summary>
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
            // Refused, reset, or the database is still starting.
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
