using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Refresh;

/// <summary>
/// Exchanges a refresh token for a new token pair, rotating the session if
/// <see cref="TokenPolicy.RotateOnUse"/> says so.
/// </summary>
/// <param name="tokenIssuer">Hashes the presented token and mints the new pair.</param>
/// <param name="users">Confirms the account behind the session still exists.</param>
/// <param name="refreshTokens">Finds the session and stores its replacement.</param>
/// <param name="unitOfWork">Commits the rotation and the replacement together.</param>
/// <param name="clock">Supplies every timestamp.</param>
public sealed class RefreshSessionHandler(
    ITokenIssuer tokenIssuer,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RefreshSession, RefreshResult>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public async Task<RefreshResult> Handle(RefreshSession command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var presentedHash = tokenIssuer.HashRefreshToken(command.RefreshToken);
        var session = await refreshTokens.FindByHashAsync(presentedHash, ct).ConfigureAwait(false);

        if (session is null || !IsAcceptable(session))
        {
            return new InvalidOrExpired();
        }

        var user = await users.FindByIdAsync(session.UserId, ct).ConfigureAwait(false);

        if (user is null)
        {
            // The session outlived its account. Indistinguishable from a bad token, on purpose.
            return new InvalidOrExpired();
        }

        var now = clock.GetUtcNow();
        var accessExpiresAt = now + TokenPolicy.AccessTokenLifetime;
        var accessToken = tokenIssuer.IssueAccessToken(user.Id, user.Email, accessExpiresAt);

        if (!TokenPolicy.RotateOnUse)
        {
            // Nothing changed, so nothing to commit: the same session keeps running and only the
            // short-lived access token is renewed.
            return new TokenPair(accessToken, command.RefreshToken, accessExpiresAt);
        }

        var replacementToken = tokenIssuer.NewRefreshToken();

        var replacement = RefreshToken.Issue(
            user.Id,
            tokenIssuer.HashRefreshToken(replacementToken),
            now + TokenPolicy.RefreshTokenLifetime,
            clock);

        await refreshTokens.AddAsync(replacement, ct).ConfigureAwait(false);

        // Guarded, not assumed: Supersede throws on a second call, and the branch below admits
        // already-superseded tokens for the length of the grace period.
        if (session.SupersededAt is null)
        {
            session.Supersede(replacement, clock);
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return new TokenPair(accessToken, replacementToken, accessExpiresAt);
    }

    /// <summary>
    /// Decides whether a stored session may still be refreshed, allowing for the rotation grace
    /// period.
    /// </summary>
    /// <param name="session">The session found by token hash.</param>
    /// <returns><see langword="true"/> when the refresh may proceed.</returns>
    /// <remarks>
    /// Rotation and concurrent tabs are in direct conflict: two tabs refreshing within the same
    /// instant means the second one presents a token that was current when it was sent and stale by
    /// the time it arrives. Without a grace window that tab is logged out for no reason the user
    /// can see. The window is short so that a genuinely replayed token — presented minutes later —
    /// is still rejected.
    /// </remarks>
    private bool IsAcceptable(RefreshToken session)
    {
        if (session.IsActive(clock))
        {
            return true;
        }

        if (!TokenPolicy.RotateOnUse || session.SupersededAt is not { } supersededAt)
        {
            return false;
        }

        var now = clock.GetUtcNow();

        return now < session.ExpiresAt && now - supersededAt <= TokenPolicy.RotationGracePeriod;
    }
}
