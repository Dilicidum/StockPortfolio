using System.Globalization;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using StackExchange.Redis;

namespace StockPortfolio.Api.HealthChecks;

/// <summary>
/// Readiness probe for Redis: <c>PING</c> over the multiplexer the rest of the app will use.
/// </summary>
/// <param name="multiplexer">The shared connection multiplexer, registered as a singleton by <c>HealthCheckExtensions</c>.</param>
/// <remarks>
/// <para>
/// Nothing consumes Redis until Phase 3. It is probed from day one anyway so the deployed topology is
/// real rather than aspirational — a Redis that has never been connected to is a Redis nobody has
/// proven the firewall rules for.
/// </para>
/// <para>
/// The multiplexer is built with <c>AbortOnConnectFail = false</c>, so resolving it never throws and a
/// Redis blip cannot take the host down at startup. The consequence is that connectivity has to be
/// established by an actual command, which is what this is.
/// </para>
/// <para>
/// As with <see cref="PostgresHealthCheck"/>, exceptions are left to propagate:
/// <c>HealthCheckService</c> converts them into the configured failure status with the exception
/// attached.
/// </para>
/// </remarks>
internal sealed class RedisHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var latency = await multiplexer.GetDatabase().PingAsync().ConfigureAwait(false);

        return HealthCheckResult.Healthy(string.Create(
            CultureInfo.InvariantCulture,
            $"Redis answered PING in {latency.TotalMilliseconds:F1} ms."));
    }
}
