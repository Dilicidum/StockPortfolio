using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>
/// The <see cref="RefreshToken"/> aggregate — one login session, and the three ways it can end.
/// </summary>
public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly UserId Owner = UserId.New();

    [Fact]
    public void Issue_NewSession_StartsActiveAndUnsuperseded()
    {
        var clock = new FakeTimeProvider(Noon);

        var token = Issue(clock);

        token.IsActive(clock).ShouldBeTrue();
        token.SupersededAt.ShouldBeNull();
        token.SupersededBy.ShouldBeNull();
        token.CreatedAt.ShouldBe(Noon);
        token.UserId.ShouldBe(Owner);
        token.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void IsActive_OneTickBeforeExpiry_ReturnsTrue()
    {
        var clock = new FakeTimeProvider(Noon);
        var token = Issue(clock, TimeSpan.FromDays(14));

        clock.Advance(TimeSpan.FromDays(14) - TimeSpan.FromTicks(1));

        token.IsActive(clock).ShouldBeTrue();
    }

    [Fact]
    public void IsActive_AtExpiry_ReturnsFalse()
    {
        var clock = new FakeTimeProvider(Noon);
        var token = Issue(clock, TimeSpan.FromDays(14));

        clock.Advance(TimeSpan.FromDays(14));

        token.IsActive(clock).ShouldBeFalse();
    }

    [Fact]
    public void IsActive_PastExpiry_ReturnsFalse()
    {
        var clock = new FakeTimeProvider(Noon);
        var token = Issue(clock, TimeSpan.FromDays(14));

        clock.Advance(TimeSpan.FromDays(15));

        token.IsActive(clock).ShouldBeFalse();
    }

    [Fact]
    public void Supersede_ActiveSession_MarksItSupersededAndLinksTheReplacement()
    {
        var clock = new FakeTimeProvider(Noon);
        var original = Issue(clock);
        var replacement = Issue(clock);

        clock.Advance(TimeSpan.FromMinutes(20));
        original.Supersede(replacement, clock);

        original.SupersededAt.ShouldBe(Noon.AddMinutes(20));
        original.SupersededBy.ShouldBe(replacement.Id);
        original.IsActive(clock).ShouldBeFalse();
        replacement.IsActive(clock).ShouldBeTrue();
    }

    [Fact]
    public void Supersede_AlreadySuperseded_Throws()
    {
        // The link is the audit chain replay detection reads. Overwriting it silently would lose
        // the evidence, so this is an invariant and it throws.
        var clock = new FakeTimeProvider(Noon);
        var original = Issue(clock);
        var first = Issue(clock);
        var second = Issue(clock);

        original.Supersede(first, clock);

        Should.Throw<InvalidOperationException>(() => original.Supersede(second, clock));
        original.SupersededBy.ShouldBe(first.Id);
    }

    [Fact]
    public void Supersede_AfterRevoke_Throws()
    {
        var clock = new FakeTimeProvider(Noon);
        var original = Issue(clock);
        var replacement = Issue(clock);

        original.Revoke(clock);

        Should.Throw<InvalidOperationException>(() => original.Supersede(replacement, clock));
    }

    [Fact]
    public void Supersede_Itself_Throws()
    {
        var clock = new FakeTimeProvider(Noon);
        var token = Issue(clock);

        Should.Throw<ArgumentException>(() => token.Supersede(token, clock));
    }

    [Fact]
    public void Supersede_ReplacementBelongingToAnotherUser_Throws()
    {
        var clock = new FakeTimeProvider(Noon);
        var original = Issue(clock);
        var someoneElses = RefreshToken.Issue(
            UserId.New(),
            [4, 5, 6],
            Noon.AddDays(14),
            clock);

        Should.Throw<ArgumentException>(() => original.Supersede(someoneElses, clock));
    }

    [Fact]
    public void Revoke_ActiveSession_EndsItWithNoReplacement()
    {
        var clock = new FakeTimeProvider(Noon);
        var token = Issue(clock);

        clock.Advance(TimeSpan.FromHours(2));
        token.Revoke(clock);

        token.SupersededAt.ShouldBe(Noon.AddHours(2));
        token.SupersededBy.ShouldBeNull();
        token.IsActive(clock).ShouldBeFalse();
    }

    [Fact]
    public void Revoke_AlreadyRevoked_Throws()
    {
        var clock = new FakeTimeProvider(Noon);
        var token = Issue(clock);

        token.Revoke(clock);

        Should.Throw<InvalidOperationException>(() => token.Revoke(clock));
    }

    [Fact]
    public void Issue_ExpiryNotInTheFuture_Throws()
    {
        var clock = new FakeTimeProvider(Noon);

        Should.Throw<ArgumentOutOfRangeException>(
            () => RefreshToken.Issue(Owner, [1, 2, 3], Noon, clock));
    }

    [Fact]
    public void Issue_EmptyTokenHash_Throws()
    {
        var clock = new FakeTimeProvider(Noon);

        Should.Throw<ArgumentException>(
            () => RefreshToken.Issue(Owner, [], Noon.AddDays(14), clock));
    }

    private static RefreshToken Issue(TimeProvider clock, TimeSpan? lifetime = null) =>
        RefreshToken.Issue(
            Owner,
            [1, 2, 3, 4],
            clock.GetUtcNow() + (lifetime ?? TimeSpan.FromDays(14)),
            clock);
}
