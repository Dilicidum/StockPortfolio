using Microsoft.Extensions.Time.Testing;
using Shouldly;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>The one definition of the canonical address, shared by User.Create and both handlers' look-ups.</summary>
public sealed class UserNormaliseEmailTests
{
    private static readonly DateTimeOffset Noon =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    private const string ValidHash = "$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0$aGFzaGhhc2g";

    /// <summary>The property the duplicate-email design rests on.</summary>
    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("Foo@Bar.com")]
    [InlineData("  Foo@Bar.com  ")]
    [InlineData("ANN.EXAMPLE+tag@Example.CO.UK")]
    [InlineData("\tada@Example.Com\r\n")]
    [InlineData("MiXeD@sub.domain.example.co.uk")]
    public void NormaliseEmail_MatchesWhatCreateStores(string input)
    {
        var result = User.Create(input, ValidHash, new FakeTimeProvider(Noon));

        result.IsT0.ShouldBeTrue($"'{input}' should have been accepted, or this case proves nothing.");

        result.AsT0.Email.ShouldBe(
            User.NormaliseEmail(input),
            "NormaliseEmail must produce exactly what Create stores. The register and login handlers "
                + "look an account up by NormaliseEmail(input); the moment the two diverge the look-up "
                + "misses, and a duplicate registration surfaces as a 500 from the unique index "
                + "instead of the 409 the handler is supposed to return.");
    }

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("  Foo@Bar.com  ")]
    [InlineData("ANN.EXAMPLE+tag@Example.CO.UK")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public void NormaliseEmail_IsIdempotent(string input)
    {
        var once = User.NormaliseEmail(input);

        User.NormaliseEmail(once).ShouldBe(
            once,
            "The stored form is fed back through NormaliseEmail on every look-up, so normalising an "
                + "already-normalised address must be a no-op.");
    }

    [Fact]
    public void NormaliseEmail_Null_ReturnsEmpty() =>
        User.NormaliseEmail(null).ShouldBe(
            string.Empty,
            "A null address is a missing one, not a crash: the repositories call this before they "
                + "have decided whether anything was supplied.");
}
