using System.Net;

using StockPortfolio.Api.IntegrationTests.Infrastructure;

using StockPortfolio.Modules.Identity.Application;

namespace StockPortfolio.Api.IntegrationTests;

/// <summary>
/// Refresh-token rotation, replay detection, and the grace window between them.
/// </summary>
/// <param name="fixture">The shared containers and host.</param>
/// <remarks>
/// <para>
/// <b>Read this before "fixing" a failure here.</b> <c>TokenPolicy.RotateOnUse</c> is on, so using a
/// refresh token supersedes it and mints a replacement — that is what makes a replayed token
/// detectable. But rotation and concurrent browser tabs are in direct conflict: two tabs refreshing
/// in the same instant means the second one presents a token that was current when it was sent and
/// stale by the time it arrived, and without a window that tab is signed out for no reason the user
/// can see. <c>TokenPolicy.RotationGracePeriod</c> is that window, currently 30 seconds.
/// </para>
/// <para>
/// So "the old token is rejected" is true <i>after</i> the grace period and deliberately false
/// inside it. Both are asserted below, and both matter: delete the grace window to make the naive
/// version of this test pass and you silently break every user with two tabs open.
/// </para>
/// <para>
/// The clock is controlled rather than slept through — a 30-second <c>Task.Delay</c> in a test suite
/// is a tax paid on every run forever. <see cref="ApiFixture.CreateHostWithClock"/> builds a second
/// host against the same containers with a <see cref="TestClock"/> in place of
/// <see cref="TimeProvider.System"/>, so these tests own their clock and no other test can see it
/// move.
/// </para>
/// </remarks>
[Collection(ApiCollectionDefinition.Name)]
public sealed class RefreshRotationTests(ApiFixture fixture)
{
    private readonly ApiFixture _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));

    /// <summary>Using a refresh token hands back a different one.</summary>
    /// <returns>A task that completes when the assertions have run.</returns>
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

    /// <summary>
    /// Inside the grace window the superseded token still works — the concurrent-tab guarantee.
    /// </summary>
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is not a bug being enshrined; it is the documented cost of rotation, and asserting it
    /// stops the window from being removed by accident.
    /// </remarks>
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
    /// <returns>A task that completes when the assertions have run.</returns>
    /// <remarks>
    /// This is the replay-detection assertion the required test list calls
    /// <c>Refresh_RotatesToken_OldOneRejected</c>. It is expressed against a controlled clock because
    /// against the system clock it would need a 30-second sleep to be true.
    /// </remarks>
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
    /// <returns>A task that completes when the assertions have run.</returns>
    [Fact]
    public async Task Refresh_WithUnknownToken_Returns401()
    {
        using var client = _fixture.CreateClient();

        using var response = await Wire.RefreshAsync(client, Convert.ToBase64String(Guid.NewGuid().ToByteArray()));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, await Wire.Describe(response));
    }
}
