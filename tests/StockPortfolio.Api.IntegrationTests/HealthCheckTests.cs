using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class HealthCheckTests(ApiFixture fixture)
{
    private const string LivenessPath = "/health/live";
    private const string ReadinessPath = "/health/ready";

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

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

        // One entry per database login, not one for "postgres": readiness once probed the Identity role alone and reported healthy regardless.
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

        // Pins the count as well as the names: a module wired nowhere contributes no check, and a per-name loop would never notice.
        report.Entries.Count.ShouldBe(
            expected.Length,
            "Every module registers its own Postgres check in its Add<M>Module. A count that drifts "
                + "means a module stopped contributing one, or an unexpected check appeared.");
    }

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
