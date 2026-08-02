using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>Mints the two kinds of token the module hands out: a signed, self-contained access token, and an.</summary>
public interface ITokenIssuer
{
    /// <summary>Signs an access token for a user.</summary>
    string IssueAccessToken(UserId userId, string email, DateTimeOffset expiresAt);

    /// <summary>Generates a refresh token.</summary>
    string NewRefreshToken();

    /// <summary>Hashes a refresh token for storage or lookup.</summary>
    byte[] HashRefreshToken(string refreshToken);
}
