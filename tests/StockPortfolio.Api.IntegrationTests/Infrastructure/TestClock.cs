namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>
/// A <see cref="TimeProvider"/> whose "now" only moves when a test moves it.
/// </summary>
/// <param name="start">The instant the clock starts at.</param>
/// <remarks>
/// <para>
/// <c>Microsoft.Extensions.TimeProvider.Testing</c> (<c>FakeTimeProvider</c>) would be the obvious
/// choice and is referenced by the Identity unit tests — but not by this project, and adding it means
/// editing the <c>.csproj</c>. Everything these tests need from a fake clock is
/// <see cref="GetUtcNow"/>, which is one override, so the twelve lines below buy the same thing
/// without touching the build.
/// </para>
/// <para>
/// The timer members are deliberately left at the base implementation: nothing under test schedules
/// work off <see cref="TimeProvider"/> in Phase 1. Phase 3's poller does, and at that point this
/// class should be replaced by <c>FakeTimeProvider</c> rather than grown.
/// </para>
/// </remarks>
internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    /// <inheritdoc/>
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="by">How far forward to move.</param>
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
