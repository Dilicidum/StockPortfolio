using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>
/// The liveness/readiness split, which is only worth having if the two endpoints actually differ.
/// </summary>
/// <param name="fixture">The shared containers and host.</param>
[Collection(ApiCollectionDefinition.Name)]
public sealed class HealthCheckTests(ApiFixture fixture)
{
    private const string LivenessPath = "/health/live";
    private const string ReadinessPath = "/health/ready";

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>Readiness is healthy, and both dependency checks are the reason.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// The HTTP body cannot carry the check names: <c>MapHealthChecks</c>'s default response writer
    /// emits the aggregate status and nothing else, so <c>/health/ready</c> answers the four
    /// characters <c>Healthy</c>. Asserting that both named checks exist and both passed therefore
    /// goes through <see cref="HealthCheckService"/> on the same host — the same registrations the
    /// endpoint runs, read where the names survive. Asserting only the body would go green if someone
    /// deleted a check.
    /// </remarks>
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
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// <para>
    /// Container Apps <i>restarts</i> a container whose liveness probe fails, so a liveness probe that
    /// touches Postgres turns a database blip into a restart loop — a degraded app becomes a down one.
    /// Readiness failing merely takes the replica out of rotation and puts it back when the dependency
    /// returns; nothing is killed.
    /// </para>
    /// <para>
    /// Proving "does not touch Postgres" needs a host where touching Postgres would be visible, so this
    /// builds a second one pointed at port 1 — reserved, and nothing listens on it. Readiness is
    /// asserted <i>unhealthy</i> on that same host, which is what makes liveness's 200 mean something:
    /// without it the test would pass equally well against a host whose dependencies were fine.
    /// </para>
    /// </remarks>
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
    /// <returns>A task that completes when the assertions have run.</returns>
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
