using Microsoft.Extensions.Diagnostics.HealthChecks;

using Npgsql;

namespace StockPortfolio.Api.HealthChecks;

/// <summary>
/// Readiness probe for Postgres: opens a connection as the Identity service role and runs
/// <c>SELECT 1</c>.
/// </summary>
/// <param name="configuration">Configuration carrying <c>ConnectionStrings:Identity</c>.</param>
/// <remarks>
/// <para>
/// The obvious implementation is <c>AddDbContextCheck&lt;IdentityDbContext&gt;()</c>, which would share
/// EF's connection pool rather than opening a second one. It is not available: <c>IdentityDbContext</c>
/// is <see langword="internal"/> to <c>Identity.Infrastructure</c>, and a generic argument the host
/// cannot name is not something a cast can rescue. Widening the module's public surface so a health
/// check can name its <c>DbContext</c> would be the tail wagging the dog, so this opens its own
/// connection instead.
/// </para>
/// <para>
/// The cost of that decision is one extra Npgsql pool. It is bounded: every connection string in this
/// repo carries <c>Maximum Pool Size=2</c>, and this check reuses the <i>same</i> string as the module,
/// so it joins the module's existing pool rather than creating a third — Npgsql keys pools by the
/// connection string, and these two are byte-identical.
/// </para>
/// <para>
/// Nothing is caught here on purpose. <c>HealthCheckService</c> already wraps every check and converts a
/// thrown exception into the check's configured failure status, carrying the exception through to the
/// response writer. A local <c>try/catch</c> would only discard the stack trace.
/// </para>
/// </remarks>
internal sealed class PostgresHealthCheck(IConfiguration configuration) : IHealthCheck
{
    /// <summary>The connection string this check probes. Deliberately the Identity module's, not the migrator's.</summary>
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
