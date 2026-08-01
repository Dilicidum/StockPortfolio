using Shouldly;

using StockPortfolio.Modules.Identity.Infrastructure.Security;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>
/// The PHC string is what makes the argon2 cost factors upgradable, so the round trip has to be exact:
/// verification re-derives with whatever <c>m</c>, <c>t</c> and <c>p</c> come back out of the column.
/// A parse that quietly returned the wrong parallelism would fail every login with no diagnostic.
/// </summary>
public sealed class PhcStringTests
{
    private static readonly byte[] Salt = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

    private static readonly byte[] Hash =
    [
        200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 213, 214, 215,
        216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231,
    ];

    [Fact]
    public void TryParse_AfterFormat_RoundTripsCostParameters()
    {
        var original = new PhcString(19456, 2, 1, Salt, Hash);

        var parsed = PhcString.TryParse(original.Format(), out var result);

        parsed.ShouldBeTrue();
        result.ShouldNotBeNull();
        result.MemoryKib.ShouldBe(19456);
        result.Iterations.ShouldBe(2);
        result.Parallelism.ShouldBe(1);
    }

    [Fact]
    public void TryParse_AfterFormat_RoundTripsSaltAndHashBytes()
    {
        var original = new PhcString(65536, 3, 4, Salt, Hash);

        PhcString.TryParse(original.Format(), out var result).ShouldBeTrue();

        result!.Salt.ShouldBe(Salt);
        result.Hash.ShouldBe(Hash);
    }

    [Fact]
    public void Format_ProducesTheCanonicalPhcLayout()
    {
        var formatted = new PhcString(19456, 2, 1, Salt, Hash).Format();

        formatted.ShouldStartWith("$argon2id$v=19$m=19456,t=2,p=1$");

        var segments = formatted.Split('$');
        segments.Length.ShouldBe(6);

        // PHC base64 is unpadded. A stray '=' would make the string non-canonical and break every other
        // argon2 implementation that might one day have to read this column.
        segments[4].ShouldNotContain("=");
        segments[5].ShouldNotContain("=");
    }

    [Theory]
    // Not a PHC string at all.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    [InlineData("argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA")]
    // Structurally short or long.
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2E")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA$extra")]
    // Wrong algorithm or version.
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=16$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA")]
    // Malformed or hostile cost parameters. m=99999999 is the one that matters: a corrupt row must not
    // be able to ask the process for 95 GiB.
    [InlineData("$argon2id$v=19$m=abc,t=2,p=1$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=19456,t=2$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=99999999,t=2,p=1$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=19456,t=0,p=1$c2FsdHNhbHRzYWx0c2E$aGFzaGhhc2hoYXNoaGFzaA")]
    // Salt and hash that are not base64, or too short to be either.
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$!!!!!!!!!!!!$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$c2E$aGFzaGhhc2hoYXNoaGFzaA")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$c2FsdHNhbHRzYWx0c2E$aGFzaA")]
    public void TryParse_MalformedInput_ReturnsFalseWithoutThrowing(string malformed)
    {
        // The input is a database column. A corrupt row must fail one login, not the process - which is
        // why this is TryParse and not a constructor that throws.
        var parsed = Should.NotThrow(() => PhcString.TryParse(malformed, out _));

        parsed.ShouldBeFalse();
    }

    [Fact]
    public void TryParse_Null_ReturnsFalseWithoutThrowing()
    {
        var parsed = Should.NotThrow(() => PhcString.TryParse(null, out _));

        parsed.ShouldBeFalse();
    }
}
