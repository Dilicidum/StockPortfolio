using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>The liveness/readiness split, which is only worth having if the two endpoints actually differ.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class HealthCheckTests(ApiFixture fixture)
{
    private const string LivenessPath = "/health/live";
    private const string ReadinessPath = "/health/ready";

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>Readiness is healthy, and both dependency checks are the reason.</summary>
    [Fact]
    public async Task Health_Ready_ReportsPostgresAndRedis()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync(ReadinessPath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.ShouldBe(nameof(HealthStatus.Healthy));

        var report = await _fixture.Services
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(TestContext.Current.CancellationToken);

        report.Status.ShouldBe(HealthStatus.Healthy);

        // One entry per database login, not one for "postgres". Readiness used to probe only the
        // Identity role, so alerts_svc could be unreachable while this reported healthy — and nothing
        // else would notice, because the poller runs on a timer rather than on a request.
        string[] expected =
        [
            "postgres-identity",
            "postgres-portfolio",
            "postgres-alerts",
            "postgres-marketdata",
            "redis",
        ];

        foreach (var name in expected)
        {
            report.Entries.Keys.ShouldContain(name);
            report.Entries[name].Status.ShouldBe(HealthStatus.Healthy);
        }

        // Pins the count as well as the names: a module wired nowhere contributes no check, and a
        // per-name loop alone would never notice its absence.
        report.Entries.Count.ShouldBe(
            expected.Length,
            "Every module registers its own Postgres check in its Add<M>Module. A count that drifts "
                + "means a module stopped contributing one, or an unexpected check appeared.");
    }

    /// <summary>Liveness answers 200 with Postgres and Redis both unreachable.</summary>
    [Fact]
    public async Task Health_Live_IgnoresDependencies()
    {
        await using var host = ApiFixture.CreateHostWithUnreachableDependencies();
        using var client = host.CreateClient();

        using var live = await client.GetAsync(LivenessPath, TestContext.Current.CancellationToken);

        live.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(live));
        (await live.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .ShouldBe(nameof(HealthStatus.Healthy));

        using var ready = await client.GetAsync(ReadinessPath, TestContext.Current.CancellationToken);

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await Wire.Describe(ready));
    }

    /// <summary>Both probes are anonymous — an authenticated probe is an unreachable probe.</summary>
    [Fact]
    public async Task Health_Endpoints_AreAnonymous()
    {
        using var client = _fixture.CreateClient();

        using var live = await client.GetAsync(LivenessPath, TestContext.Current.CancellationToken);
        using var ready = await client.GetAsync(ReadinessPath, TestContext.Current.CancellationToken);

        live.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        ready.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
    }
}
