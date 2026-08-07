using System.Net;
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

namespace StockPortfolio.Api.IntegrationTests;

[Collection(ApiCollectionDefinition.Name)]
public sealed class HealthCheckTests(ApiFixture fixture)
{
    private const string LivenessPath = "/health/live";
    private const string ReadinessPath = "/health/ready";
    private const string StartupPath = "/health/startup";
    private const string DetailPath = "/api/health/detail";

    private const string RedisCheck = "redis";
    private const string FeedCheck = "marketdata-feed";
    private const string MigrationsCheck = "migrations";

    private static readonly string[] DatabaseChecks =
    [
        "postgres-identity",
        "postgres-portfolio",
        "postgres-alerts",
        "postgres-marketdata",
    ];

    private static readonly string[] ReadyComponentNames = [.. DatabaseChecks, RedisCheck];

    private static readonly string[] DetailComponentNames = [.. DatabaseChecks, RedisCheck, FeedCheck];

    private static readonly string[] StartupComponentNames = [MigrationsCheck];

    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    [Fact]
    public void HealthChecks_AreRegisteredWithTheTagsTheirProbesSelectOn()
    {
        var registrations = _fixture.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value
            .Registrations
            .ToDictionary(registration => registration.Name, StringComparer.Ordinal);

        // One entry per database login, not one for "postgres": readiness once probed the Identity role alone and reported healthy regardless.
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["postgres-identity"] = ["ready", "detail"],
            ["postgres-portfolio"] = ["ready", "detail"],
            ["postgres-alerts"] = ["ready", "detail"],
            ["postgres-marketdata"] = ["ready", "detail"],
            [RedisCheck] = ["ready", "detail"],

            // Not "ready": a rejected provider key reports Unhealthy, and readiness answering 503 for that would withdraw the replica.
            [FeedCheck] = ["detail"],

            // Not "ready" either: it is a database round trip that only has to pass once, at start.
            [MigrationsCheck] = ["startup"],
        };

        foreach (var (name, tags) in expected)
        {
            registrations.ShouldContainKey(name);
            registrations[name].Tags.Order(StringComparer.Ordinal)
                .ShouldBe(tags.Order(StringComparer.Ordinal), ignoreOrder: false);
        }

        // Pins the count as well as the names: a check registered nowhere contributes nothing, and a per-name loop would never notice.
        registrations.Count.ShouldBe(
            expected.Count,
            "Every module registers its own Postgres check in its Add<M>Module, MarketData adds the feed "
                + "check, and the host adds Redis and migrations. A count that drifts means a check "
                + "stopped being registered, or an unexpected one appeared. Never soften this to non-empty.");
    }

    [Fact]
    public async Task Health_Ready_ReportsEveryDatabaseLoginAndRedis()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync(ReadinessPath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var report = await ReadReportAsync(response);

        report.Status.ShouldBe(nameof(HealthStatus.Healthy));

        foreach (var name in DatabaseChecks)
        {
            report.StatusOf(name).ShouldBe(nameof(HealthStatus.Healthy));
        }

        report.StatusOf(RedisCheck).ShouldBe(nameof(HealthStatus.Healthy));

        // Readiness selects on the "ready" tag, so the feed and migration checks must be absent from this body.
        report.Names().ShouldBe(ReadyComponentNames, ignoreOrder: true);
    }

    [Fact]
    public async Task Health_Ready_WithRedisDown_StaysInRotation()
    {
        await using var host = _fixture.CreateHostWithRedisDown();
        using var client = host.CreateClient();

        using var response = await client.GetAsync(ReadinessPath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "A cache outage must not take the replica out of rotation. Redis is registered with "
                + "failureStatus Degraded, which the framework maps to 200; the default Unhealthy made "
                + "this 503, Container Apps withdrew every replica, and the API became unreachable. "
                + await Wire.Describe(response));

        var report = await ReadReportAsync(response);

        // The three-state value explicitly: two states would let Degraded be reported as Unhealthy and this test would still see 200.
        report.StatusOf(RedisCheck).ShouldBe(nameof(HealthStatus.Degraded));
        report.Status.ShouldBe(nameof(HealthStatus.Degraded));

        foreach (var name in DatabaseChecks)
        {
            report.StatusOf(name).ShouldBe(nameof(HealthStatus.Healthy));
        }
    }

    [Fact]
    public async Task Health_Startup_RunsTheMigrationsCheckAndNothingElse()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync(StartupPath, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var report = await ReadReportAsync(response);

        report.Status.ShouldBe(nameof(HealthStatus.Healthy));

        // The fixture migrates before the host boots, so pending is empty; the name list is what catches a startup probe that has quietly become a second readiness probe.
        report.Names().ShouldBe(StartupComponentNames, ignoreOrder: true);
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

        // Unhealthy still means 503 on readiness; that is what takes a genuinely broken replica out of rotation.
        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable, await Wire.Describe(ready));
    }

    [Fact]
    public async Task Health_Endpoints_AreAnonymousExceptTheDetail()
    {
        using var client = _fixture.CreateClient();

        using var live = await client.GetAsync(LivenessPath, TestContext.Current.CancellationToken);
        using var ready = await client.GetAsync(ReadinessPath, TestContext.Current.CancellationToken);
        using var startup = await client.GetAsync(StartupPath, TestContext.Current.CancellationToken);
        using var detail = await client.GetAsync(DetailPath, TestContext.Current.CancellationToken);

        live.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        ready.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);
        startup.StatusCode.ShouldNotBe(HttpStatusCode.Unauthorized);

        detail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(detail));
    }

    [Fact]
    public async Task Health_Detail_ReportsEveryComponentAndTheFeedFacts()
    {
        using var client = _fixture.CreateClient();

        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("health-detail"));

        using var response = await Wire.SendAsync(client, HttpMethod.Get, DetailPath, tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(response));

        var report = await ReadReportAsync(response);

        report.Names().ShouldBe(DetailComponentNames, ignoreOrder: true);

        var feed = report.Component(FeedCheck).GetProperty("data");

        feed.GetProperty("provider").GetString().ShouldBe(ApiFixture.FakeProviderName);
        feed.GetProperty("providerKeyRejected").GetBoolean().ShouldBeFalse();
        feed.TryGetProperty("tickersTargeted", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Health_Detail_WithRedisDown_Answers200NotServiceUnavailable()
    {
        await using var host = _fixture.CreateHostWithRedisDown();
        using var client = host.CreateClient();

        var tokens = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("health-detail-redis-down"));

        using var response = await Wire.SendAsync(client, HttpMethod.Get, DetailPath, tokens.AccessToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "A route whose job is to report that a dependency is unhealthy cannot use that failure as "
                + "its own status, or the browser's health card goes blank exactly when it becomes "
                + "useful. Every status maps to 200 on this route. "
                + await Wire.Describe(response));

        var report = await ReadReportAsync(response);

        // Unhealthy overall — the heartbeat is unreadable with the cache down — and still a 200.
        report.Status.ShouldBe(nameof(HealthStatus.Unhealthy));
        report.StatusOf(RedisCheck).ShouldBe(nameof(HealthStatus.Degraded));
        report.StatusOf(FeedCheck).ShouldBe(nameof(HealthStatus.Unhealthy));

        foreach (var name in DatabaseChecks)
        {
            report.StatusOf(name).ShouldBe(nameof(HealthStatus.Healthy));
        }
    }

    private static async Task<HealthBody> ReadReportAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(json);

        // Cloned, so the body outlives the document rather than making this helper disposable.
        return new HealthBody(document.RootElement.Clone());
    }

    private sealed record HealthBody(JsonElement Root)
    {
        public string? Status => Root.GetProperty("status").GetString();

        public IReadOnlyList<string> Names() =>
        [
            .. Root.GetProperty("components")
                .EnumerateArray()
                .Select(component => component.GetProperty("name").GetString() ?? string.Empty),
        ];

        public JsonElement Component(string name) =>
            Root.GetProperty("components")
                .EnumerateArray()
                .Single(component => string.Equals(
                    component.GetProperty("name").GetString(),
                    name,
                    StringComparison.Ordinal));

        public string? StatusOf(string name) => Component(name).GetProperty("status").GetString();
    }
}
