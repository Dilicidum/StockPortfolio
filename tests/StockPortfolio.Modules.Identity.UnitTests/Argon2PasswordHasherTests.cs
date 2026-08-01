using Shouldly;

using StockPortfolio.Modules.Identity.Infrastructure.Security;

namespace StockPortfolio.Modules.Identity.UnitTests;

/// <summary>
/// Each case here is ~40 ms of deliberate work — that is the point of a 19 MiB memory-hard KDF — so the
/// suite stays small and covers only the properties that would be silent failures: the round trip, a
/// wrong password answering false instead of throwing, and the salt actually being random.
/// </summary>
public sealed class Argon2PasswordHasherTests
{
    private const string Password = "correct horse battery staple";

    private readonly Argon2PasswordHasher _hasher = new();

    [Fact]
    public void Verify_WithTheHashedPassword_ReturnsTrue()
    {
        var encoded = _hasher.Hash(Password);

        _hasher.Verify(Password, encoded).ShouldBeTrue();
    }

    [Fact]
    public void Verify_WithTheWrongPassword_ReturnsFalse()
    {
        var encoded = _hasher.Hash(Password);

        // Not an exception: a failed verification is an expected outcome of a login, and throwing here
        // would turn every mistyped password into a 500.
        var verified = Should.NotThrow(() => _hasher.Verify("Correct horse battery staple", encoded));

        verified.ShouldBeFalse();
    }

    [Fact]
    public void Verify_WithAMalformedStoredHash_ReturnsFalseWithoutThrowing()
    {
        var verified = Should.NotThrow(() => _hasher.Verify(Password, "not-a-phc-string"));

        verified.ShouldBeFalse();
    }

    [Fact]
    public void Hash_CalledTwiceWithTheSamePassword_ProducesDifferentHashes()
    {
        // If these matched, the salt would be constant or absent, and the whole users table would fall to
        // one rainbow table. This is the assertion that proves RandomNumberGenerator is actually in play.
        var first = _hasher.Hash(Password);
        var second = _hasher.Hash(Password);

        first.ShouldNotBe(second);

        // Both must still verify - a random salt is only useful if it is stored with the digest.
        _hasher.Verify(Password, first).ShouldBeTrue();
        _hasher.Verify(Password, second).ShouldBeTrue();
    }

    [Fact]
    public void Hash_EncodesTheOwaspCostParameters()
    {
        var encoded = _hasher.Hash(Password);

        PhcString.TryParse(encoded, out var parsed).ShouldBeTrue();

        parsed!.MemoryKib.ShouldBe(Argon2PasswordHasher.MemorySizeKib);
        parsed.Iterations.ShouldBe(Argon2PasswordHasher.Iterations);
        parsed.Parallelism.ShouldBe(Argon2PasswordHasher.DegreeOfParallelism);
        parsed.Salt.Length.ShouldBe(Argon2PasswordHasher.SaltLengthBytes);
        parsed.Hash.Length.ShouldBe(Argon2PasswordHasher.HashLengthBytes);
    }

    [Fact]
    public void DummyHash_CarriesTheSameCostParametersAsARealHash()
    {
        // Login verifies against DummyHash when the email is unknown, so that path only hides account
        // existence if it does the same amount of work. Matching parameters is what makes the timing match.
        PhcString.TryParse(_hasher.DummyHash, out var dummy).ShouldBeTrue();
        PhcString.TryParse(_hasher.Hash(Password), out var real).ShouldBeTrue();

        dummy!.MemoryKib.ShouldBe(real!.MemoryKib);
        dummy.Iterations.ShouldBe(real.Iterations);
        dummy.Parallelism.ShouldBe(real.Parallelism);
    }

    [Fact]
    public void Verify_AgainstDummyHash_ReturnsFalseForAnyPassword()
    {
        _hasher.Verify(Password, _hasher.DummyHash).ShouldBeFalse();
        _hasher.Verify("", _hasher.DummyHash).ShouldBeFalse();
    }
}
