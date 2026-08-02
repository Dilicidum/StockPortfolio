using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>The User aggregate: what it normalises, what it refuses, and where its clock comes from.</summary>
public sealed class UserTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private const string ValidHash = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2g";

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("missing-domain@")]
    [InlineData("@missing-local.com")]
    [InlineData("no-dot-in-host@localhost")]
    [InlineData("two@at@signs.com")]
    [InlineData("spaces in@example.com")]
    [InlineData("trailing-dot@example.com.")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_MalformedEmail_ReturnsValidationFailed(string email)
    {
        var result = User.Create(email, ValidHash, new FakeTimeProvider(Noon));

        result.IsT1.ShouldBeTrue($"'{email}' should not have been accepted as an email address.");
        result.AsT1.Field.ShouldBe("email");
    }

    [Theory]
    [InlineData("Foo@Bar.com", "foo@bar.com")]
    [InlineData("  Foo@Bar.com  ", "foo@bar.com")]
    [InlineData("ANN.EXAMPLE+tag@Example.CO.UK", "ann.example+tag@example.co.uk")]
    public void Create_MixedCaseOrPaddedEmail_StoresTheNormalisedForm(string input, string expected)
    {
        var result = User.Create(input, ValidHash, new FakeTimeProvider(Noon));

        result.IsT0.ShouldBeTrue();
        result.AsT0.Email.ShouldBe(expected);
    }

    [Fact]
    public void Create_Always_TakesCreatedAtFromTheInjectedClock()
    {
        // Deliberately not "now": if the entity reached for DateTimeOffset.UtcNow this assertion would fail.
        var clock = new FakeTimeProvider(Noon);

        var result = User.Create("ann@example.com", ValidHash, clock);

        result.IsT0.ShouldBeTrue();
        result.AsT0.CreatedAt.ShouldBe(Noon);
    }

    [Fact]
    public void Create_ClockAdvancedBetweenCalls_GivesEachUserItsOwnCreatedAt()
    {
        var clock = new FakeTimeProvider(Noon);

        var first = User.Create("first@example.com", ValidHash, clock).AsT0;
        clock.Advance(TimeSpan.FromHours(3));
        var second = User.Create("second@example.com", ValidHash, clock).AsT0;

        first.CreatedAt.ShouldBe(Noon);
        second.CreatedAt.ShouldBe(Noon.AddHours(3));
    }

    [Fact]
    public void Create_Always_AssignsAnId()
    {
        var result = User.Create("ann@example.com", ValidHash, new FakeTimeProvider(Noon));

        result.IsT0.ShouldBeTrue();
        result.AsT0.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Create_TwoUsers_GetDistinctIds()
    {
        var clock = new FakeTimeProvider(Noon);

        var first = User.Create("first@example.com", ValidHash, clock).AsT0;
        var second = User.Create("second@example.com", ValidHash, clock).AsT0;

        first.Id.ShouldNotBe(second.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankPasswordHash_Throws(string? passwordHash)
    {
        // An invariant, not a context failure: a blank hash would let anything authenticate, and no caller.
        Should.Throw<ArgumentException>(
            () => User.Create("ann@example.com", passwordHash!, new FakeTimeProvider(Noon)));
    }

    [Fact]
    public void Create_NullClock_Throws()
    {
        Should.Throw<ArgumentNullException>(
            () => User.Create("ann@example.com", ValidHash, null!));
    }

    [Fact]
    public void ChangePasswordHash_NewHash_ReplacesTheStoredOne()
    {
        var user = User.Create("ann@example.com", ValidHash, new FakeTimeProvider(Noon)).AsT0;

        user.ChangePasswordHash("$argon2id$v=19$m=19456,t=2,p=1$bmV3c2FsdG5ld3NhbHQ$bmV3aGFzaA");

        user.PasswordHash.ShouldNotBe(ValidHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ChangePasswordHash_BlankHash_Throws(string? newHash)
    {
        var user = User.Create("ann@example.com", ValidHash, new FakeTimeProvider(Noon)).AsT0;

        Should.Throw<ArgumentException>(() => user.ChangePasswordHash(newHash!));
        user.PasswordHash.ShouldBe(ValidHash);
    }
}
