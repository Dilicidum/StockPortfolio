namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>
/// Turns a password into something safe to store, and checks one against a stored hash.
/// </summary>
/// <remarks>
/// Implemented in <c>.Infrastructure</c> over Argon2id. The interface lives here so the handlers
/// depend on the operation, not on the algorithm or its package.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>
    /// Gets a fixed, valid hash of nothing anyone knows, for verifying against when the account
    /// does not exist.
    /// </summary>
    /// <remarks>
    /// Not decoration. Returning early on "no such user" makes the login endpoint answer faster for
    /// unknown addresses than for known ones, which is an account-enumeration oracle that no amount
    /// of care in the response body can close. Verifying against this value spends the same
    /// milliseconds either way.
    /// </remarks>
    string DummyHash { get; }

    /// <summary>Hashes a password for storage.</summary>
    /// <param name="password">The plaintext password.</param>
    /// <returns>A PHC-encoded hash carrying its own parameters and salt.</returns>
    string Hash(string password);

    /// <summary>Checks a password against a stored hash.</summary>
    /// <param name="password">The plaintext password to check.</param>
    /// <param name="encodedHash">The PHC-encoded hash to check it against.</param>
    /// <returns><see langword="true"/> when the password produces that hash.</returns>
    bool Verify(string password, string encodedHash);
}
