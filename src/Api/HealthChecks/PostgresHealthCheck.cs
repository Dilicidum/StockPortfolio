using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace StockPortfolio.Api.HealthChecks;

/// <summary>Readiness probe for Postgres: opens a connection as the Identity service role and runs SELECT 1.</summary>
internal sealed class PostgresHealthCheck(IConfiguration configuration) : IHealthCheck
{
    /// <summary>The connection string this check probes.</summary>
    internal const string ConnectionStringName = "Identity";

    private const string ProbeSql = "SELECT 1";

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy(
                $"Connection string '{ConnectionStringName}' is not configured.");
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = new NpgsqlCommand(ProbeSql, connection);
        _ = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return HealthCheckResult.Healthy("Postgres accepted a connection and answered SELECT 1.");
    }
}
