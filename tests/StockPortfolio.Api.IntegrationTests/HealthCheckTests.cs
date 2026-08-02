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
        report.Entries.Keys.ShouldContain("postgres");
        report.Entries.Keys.ShouldContain("redis");
        report.Entries["postgres"].Status.ShouldBe(HealthStatus.Healthy);
        report.Entries["redis"].Status.ShouldBe(HealthStatus.Healthy);
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
