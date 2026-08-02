using System.Globalization;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using StackExchange.Redis;

namespace StockPortfolio.Api.HealthChecks;

/// <summary>Readiness probe for Redis: PING over the multiplexer the rest of the app will use.</summary>
internal sealed class RedisHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var latency = await multiplexer.GetDatabase().PingAsync();

        return HealthCheckResult.Healthy(string.Create(
            CultureInfo.InvariantCulture,
            $"Redis answered PING in {latency.TotalMilliseconds:F1} ms."));
    }
}
