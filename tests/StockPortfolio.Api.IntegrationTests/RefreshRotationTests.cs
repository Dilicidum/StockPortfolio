using System.Net;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

using StockPortfolio.Modules.Identity.Application;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>Refresh-token rotation, replay detection, and the grace window between them.</summary>
[Collection(ApiCollectionDefinition.Name)]
public sealed class RefreshRotationTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>Using a refresh token hands back a different one.</summary>
    [Fact]
    public async Task Refresh_RotatesToken_ReturnsNewPair()
    {
        using var client = _fixture.CreateClient();

        var issued = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("rotate"));

        using var refreshed = await Wire.RefreshAsync(client, issued.RefreshToken);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(refreshed));

        var rotated = await Wire.ReadTokensAsync(refreshed);

        rotated.RefreshToken.ShouldNotBe(issued.RefreshToken);
        rotated.AccessToken.ShouldNotBeNullOrWhiteSpace();

        // The replacement works, so the session survived the rotation.
        using var again = await Wire.RefreshAsync(client, rotated.RefreshToken);
        again.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(again));
    }

    /// <summary>Inside the grace window the superseded token still works — the concurrent-tab guarantee.</summary>
    [Fact]
    public async Task Refresh_WithinGracePeriod_StillAcceptsSupersededToken()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await using var host = _fixture.CreateHostWithClock(clock);
        using var client = host.CreateClient();

        var issued = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("grace-inside"));

        using var first = await Wire.RefreshAsync(client, issued.RefreshToken);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(first));

        // Well inside the window, and the clock never moves on its own here.
        clock.Advance(TokenPolicy.RotationGracePeriod / 2);

        using var replayed = await Wire.RefreshAsync(client, issued.RefreshToken);

        replayed.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(replayed));
    }

    /// <summary>Past the grace window the superseded token is rejected.</summary>
    [Fact]
    public async Task Refresh_AfterGracePeriod_RejectsSupersededToken()
    {
        var clock = new TestClock(DateTimeOffset.UtcNow);
        await using var host = _fixture.CreateHostWithClock(clock);
        using var client = host.CreateClient();

        var issued = await Wire.RegisterSucceedsAsync(client, Wire.UniqueEmail("grace-outside"));

        using var first = await Wire.RefreshAsync(client, issued.RefreshToken);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(first));

        var rotated = await Wire.ReadTokensAsync(first);

        clock.Advance(TokenPolicy.RotationGracePeriod + TimeSpan.FromSeconds(1));

        using var replayed = await Wire.RefreshAsync(client, issued.RefreshToken);
        replayed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(replayed));

        // The replacement is unaffected: only the superseded half of the chain died.
        using var current = await Wire.RefreshAsync(client, rotated.RefreshToken);
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await Wire.Describe(current));
    }

    /// <summary>A refresh token that was never issued is rejected.</summary>
    [Fact]
    public async Task Refresh_WithUnknownToken_Returns401()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.RefreshAsync(client, Convert.ToBase64String(Guid.NewGuid().ToByteArray()));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }
}
