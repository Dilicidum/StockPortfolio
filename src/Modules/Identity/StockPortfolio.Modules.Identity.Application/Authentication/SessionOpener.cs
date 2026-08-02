using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;

namespace StockPortfolio.Modules.Identity.Application.Authentication;

/// <summary>The one place a session is opened: register, login and refresh all issue their token pair here.</summary>
public sealed class SessionOpener(
    ITokenIssuer tokenIssuer,
    IRefreshTokenRepository refreshTokens,
    TimeProvider clock)
{
    /// <summary>Opens a brand-new session for a user, leaving any other session of theirs alone.</summary>
    public Task<TokenPair> OpenAsync(User user, CancellationToken ct) =>
        IssueAsync(user, replacing: null, ct);

    /// <summary>Opens a session that takes over from an existing one, retiring it in the same commit.</summary>
    public Task<TokenPair> RotateAsync(User user, RefreshToken replacing, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(replacing);

        return IssueAsync(user, replacing, ct);
    }

    private async Task<TokenPair> IssueAsync(User user, RefreshToken? replacing, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);

        // One reading of the clock serves both lifetimes, so the two expiries cannot drift apart.
        var now = clock.GetUtcNow();
        var accessExpiresAt = now + TokenPolicy.AccessTokenLifetime;

        // The instant baked into the JWT is the same one returned as AccessExpiresAt.
        var accessToken = tokenIssuer.IssueAccessToken(user.Id, user.Email, accessExpiresAt);

        // The token hashed into the row below is the same one handed to the caller.
        var refreshToken = tokenIssuer.NewRefreshToken();

        var session = RefreshToken.Issue(
            user.Id,
            tokenIssuer.HashRefreshToken(refreshToken),
            now + TokenPolicy.RefreshTokenLifetime,
            clock);

        // Guarded, not assumed: Supersede throws on a second call, and the grace period admits a superseded session.
        if (replacing is { SupersededAt: null })
        {
            replacing.Supersede(session, clock);
        }

        // Stamped before the insert so one commit covers both — losing the supersede would leave two live sessions.
        await refreshTokens.AddAsync(session, ct);

        return new TokenPair(accessToken, refreshToken, accessExpiresAt);
    }
}
