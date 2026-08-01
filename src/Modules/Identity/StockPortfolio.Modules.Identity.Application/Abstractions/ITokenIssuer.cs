using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>
/// Mints the two kinds of token the module hands out: a signed, self-contained access token, and
/// an opaque, high-entropy refresh token.
/// </summary>
public interface ITokenIssuer
{
    /// <summary>Signs an access token for a user.</summary>
    /// <param name="userId">Goes into the <c>sub</c> claim.</param>
    /// <param name="email">Goes into the <c>email</c> claim.</param>
    /// <param name="expiresAt">Goes into <c>exp</c>.</param>
    /// <returns>The compact-serialised JWT.</returns>
    string IssueAccessToken(UserId userId, string email, DateTimeOffset expiresAt);

    /// <summary>Generates a refresh token.</summary>
    /// <returns>32 cryptographically random bytes, base64url encoded.</returns>
    string NewRefreshToken();

    /// <summary>Hashes a refresh token for storage or lookup.</summary>
    /// <param name="refreshToken">The token string as the client holds it.</param>
    /// <returns>The 32-byte SHA-256 digest.</returns>
    /// <remarks>
    /// Deterministic and unsalted by design — the stored hash is also the lookup key. That is safe
    /// only because the input is uniformly random; never reuse this shape for a password.
    /// </remarks>
    byte[] HashRefreshToken(string refreshToken);
}
