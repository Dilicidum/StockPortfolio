using OneOf;
using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;

/// <summary>Exchanges a refresh token for a new token pair, rotating the session if RotateOnUse says so.</summary>
public sealed class RefreshSessionCommandHandler(
    ITokenIssuer tokenIssuer,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    TimeProvider clock) : ICommandHandler<RefreshSessionCommand, OneOf<TokenPair, InvalidOrExpired>>
{
    /// <inheritdoc/>
    public async Task<OneOf<TokenPair, InvalidOrExpired>> Handle(RefreshSessionCommand command, CancellationToken ct)
    {
        var presentedHash = tokenIssuer.HashRefreshToken(command.RefreshToken);
        var session = await refreshTokens.FindByHashAsync(presentedHash, ct);

        if (session is null || !IsAcceptable(session))
        {
            return new InvalidOrExpired();
        }

        var user = await users.FindByIdAsync(session.UserId, ct);

        if (user is null)
        {
            // The session outlived its account.
            return new InvalidOrExpired();
        }

        var now = clock.GetUtcNow();
        var accessExpiresAt = now + TokenPolicy.AccessTokenLifetime;
        var accessToken = tokenIssuer.IssueAccessToken(user.Id, user.Email, accessExpiresAt);

        if (!TokenPolicy.RotateOnUse)
        {
            // Nothing changed, so nothing to commit: the same session keeps running.
            return new TokenPair(accessToken, command.RefreshToken, accessExpiresAt);
        }

        var replacementToken = tokenIssuer.NewRefreshToken();

        var replacement = RefreshToken.Issue(
            user.Id,
            tokenIssuer.HashRefreshToken(replacementToken),
            now + TokenPolicy.RefreshTokenLifetime,
            clock);

        // Guarded, not assumed: Supersede throws on a second call, and the grace period admits a superseded session.
        if (session.SupersededAt is null)
        {
            session.Supersede(replacement, clock);
        }

        // Stamped before the insert so one commit covers both — losing the supersede would leave two live sessions.
        await refreshTokens.AddAsync(replacement, ct);

        return new TokenPair(accessToken, replacementToken, accessExpiresAt);
    }

    /// <summary>Decides whether a stored session may still be refreshed, allowing for the rotation grace period.</summary>
    private bool IsAcceptable(RefreshToken session)
    {
        if (session.IsActive(clock))
        {
            return true;
        }

        // SupersededBy separates the two ways a session ends: rotation names its replacement, logout leaves it
        // null. Only rotation earns the grace window — otherwise logging out would not take effect for 30s.
        if (!TokenPolicy.RotateOnUse
            || session.SupersededBy is null
            || session.SupersededAt is not { } supersededAt)
        {
            return false;
        }

        var now = clock.GetUtcNow();

        return now < session.ExpiresAt && now - supersededAt <= TokenPolicy.RotationGracePeriod;
    }
}
