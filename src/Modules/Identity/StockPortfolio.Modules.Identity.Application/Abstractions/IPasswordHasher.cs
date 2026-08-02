namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>Turns a password into something safe to store, and checks one against a stored hash.</summary>
public interface IPasswordHasher
{
    /// <summary>Gets a fixed hash of nothing anyone knows, to verify against when the account does not exist.</summary>
    string DummyHash { get; }

    /// <summary>Hashes a password for storage.</summary>
    string Hash(string password);

    /// <summary>Checks a password against a stored hash.</summary>
    bool Verify(string password, string encodedHash);
}
