using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace StockPortfolio.Host.Health;

/// <summary>Unhealthy while any registered context still has a migration to apply; the provider handed in is the health check's own scope.</summary>
internal sealed class PendingMigrationsHealthCheck(
    IServiceProvider scoped,
    IReadOnlyList<Type> contextTypes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>(StringComparer.Ordinal);
        var outstanding = new List<string>();

        foreach (var contextType in contextTypes)
        {
            var dbContext = (DbContext)scoped.GetRequiredService(contextType);

            IReadOnlyList<string> pending;

            try
            {
                pending = [.. await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)];
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return HealthCheckResult.Unhealthy(
                    $"{contextType.Name} could not be asked which migrations are pending.",
                    ex,
                    data);
            }

            data[contextType.Name] = pending.Count;

            if (pending.Count > 0)
            {
                outstanding.Add($"{contextType.Name}: {string.Join(", ", pending)}");
            }
        }

        return outstanding.Count == 0
            ? HealthCheckResult.Healthy("Every context is at its latest migration.", data)
            : HealthCheckResult.Unhealthy(
                "Migrations are still pending. " + string.Join(" | ", outstanding),
                data: data);
    }
}
