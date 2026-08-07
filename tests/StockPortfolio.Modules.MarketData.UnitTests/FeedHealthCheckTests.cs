using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

using Shouldly;

using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Infrastructure.Health;
using StockPortfolio.Modules.MarketData.Infrastructure.Polling;

namespace StockPortfolio.Tests;

public sealed class FeedHealthCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 15, 0, 0, TimeSpan.Zero);

    private const string IntervalSeconds = "60";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Check_CycleWithNoTargetsAtAll_IsHealthy()
    {
        var result = await Run(new FeedHealth(Now - TimeSpan.FromSeconds(30), 0, 0, "Fake", false));

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Check_NoTargetsButTheHeartbeatStopped_IsUnhealthy()
    {
        var result = await Run(new FeedHealth(Now - TimeSpan.FromHours(6), 0, 0, "Fake", false));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Check_RecentCycle_IsHealthy()
    {
        var result = await Run(new FeedHealth(Now - TimeSpan.FromMinutes(1), 4, 4, "Fake", false));

        result.Status.ShouldBe(HealthStatus.Healthy);
    }

    [Fact]
    public async Task Check_CycleBetweenThreeAndTenIntervalsOld_IsDegraded()
    {
        var result = await Run(new FeedHealth(Now - TimeSpan.FromMinutes(5), 4, 4, "Fake", false));

        result.Status.ShouldBe(HealthStatus.Degraded);
    }

    [Fact]
    public async Task Check_NoCycleHasEverFinished_IsUnhealthy()
    {
        var result = await Run(new FeedHealth(null, 0, 0, "Fake", false));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Check_ProviderRejectedTheKey_IsUnhealthyEvenWhileThePollerKeepsUp()
    {
        var result = await Run(new FeedHealth(Now - TimeSpan.FromSeconds(30), 4, 4, "Finnhub", true));

        result.Status.ShouldBe(HealthStatus.Unhealthy);
        result.Description.ShouldNotBeNull().ShouldContain("rejected");
    }

    [Fact]
    public async Task Check_ReportsTheFactsTheHealthPanelDisplays()
    {
        var result = await Run(new FeedHealth(Now - TimeSpan.FromSeconds(30), 7, 5, "Finnhub", false));

        result.Data["provider"].ShouldBe("Finnhub");
        result.Data["tickersTargeted"].ShouldBe(7);
        result.Data["tickersStored"].ShouldBe(5);
        result.Data["providerKeyRejected"].ShouldBe(false);
        result.Data.ShouldContainKey("lastCycleAt");
    }

    private static Task<HealthCheckResult> Run(FeedHealth health)
    {
        var polling = PollingOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [PollingOptions.SectionName + ":IntervalSeconds"] = IntervalSeconds,
            })
            .Build());

        var check = new FeedHealthCheck(new StubFeedHealth(health), polling, new FakeTimeProvider(Now));

        return check.CheckHealthAsync(new HealthCheckContext(), Ct);
    }

    private sealed class StubFeedHealth(FeedHealth health) : IFeedHealth
    {
        public Task<FeedHealth> GetFeedHealthAsync(CancellationToken ct) => Task.FromResult(health);
    }
}
