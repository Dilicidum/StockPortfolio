using System.Globalization;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Domain;
using StockPortfolio.Modules.MarketData.Infrastructure.Polling;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Health;

/// <summary>The poll heartbeat as a health check; the rule answers on timing alone, so a rejected key is applied on top of its answer.</summary>
internal sealed class FeedHealthCheck(IFeedHealth feed, PollingOptions polling, TimeProvider clock) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var health = await feed.GetFeedHealthAsync(cancellationToken);

        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["tickersTargeted"] = health.TickersTargeted,
            ["tickersStored"] = health.TickersStored,
            ["provider"] = health.ProviderName,
            ["providerKeyRejected"] = health.ProviderKeyRejected,
        };

        if (health.LastCycleAt is { } lastCycleAt)
        {
            data["lastCycleAt"] = lastCycleAt.ToString("O", CultureInfo.InvariantCulture);
        }

        // FeedHealthRule takes timing facts only, so this arm is the whole of how a rejected key reaches Unhealthy.
        if (health.ProviderKeyRejected)
        {
            return HealthCheckResult.Unhealthy(
                "The market-data provider rejected the configured API key. Quotes fall back to the last "
                    + "known price until a working key is configured.",
                data: data);
        }

        var verdict = FeedHealthRule.Evaluate(
            health.LastCycleAt,
            health.TickersTargeted,
            health.TickersStored,
            polling.Interval,
            clock.GetUtcNow());

        return verdict switch
        {
            FeedVerdict.Healthy => HealthCheckResult.Healthy(
                "The quote poller is keeping up with its interval.",
                data),

            FeedVerdict.Degraded => HealthCheckResult.Degraded(
                "The quote poller is behind its interval; alert windows may have a gap in them.",
                data: data),

            _ => HealthCheckResult.Unhealthy(
                "The quote poller has not finished a cycle recently, so no alert can fire.",
                data: data),
        };
    }
}
