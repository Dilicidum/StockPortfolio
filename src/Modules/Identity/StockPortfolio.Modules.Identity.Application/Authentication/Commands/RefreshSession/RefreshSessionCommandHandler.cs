using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;

/// <summary>Exchanges a refresh token for a new token pair, rotating the session if RotateOnUse says so.</summary>
public sealed class RefreshSessionCommandHandler(
    ITokenIssuer tokenIssuer,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RefreshSessionCommand, RefreshSessionResult>
{
    /// <inheritdoc/>
    public async Task<RefreshSessionResult> Handle(RefreshSessionCommand command, CancellationToken ct)
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
            // The session outlived its account.
            return new InvalidOrExpired();
        }

        var now = clock.GetUtcNow();
        var accessExpiresAt = now + TokenPolicy.AccessTokenLifetime;
        var accessToken = tokenIssuer.IssueAccessToken(user.Id, user.Email, accessExpiresAt);

        if (!TokenPolicy.RotateOnUse)
        {
            // Nothing changed, so nothing to commit: the same session keeps running and only the short-lived.
            return new TokenPair(accessToken, command.RefreshToken, accessExpiresAt);
        }

        var replacementToken = tokenIssuer.NewRefreshToken();

        var replacement = RefreshToken.Issue(
            user.Id,
            tokenIssuer.HashRefreshToken(replacementToken),
            now + TokenPolicy.RefreshTokenLifetime,
            clock);

        await refreshTokens.AddAsync(replacement, ct).ConfigureAwait(false);

        // Guarded, not assumed: Supersede throws on a second call, and the branch below admits.
        if (session.SupersededAt is null)
        {
            session.Supersede(replacement, clock);
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return new TokenPair(accessToken, replacementToken, accessExpiresAt);
    }

    /// <summary>Decides whether a stored session may still be refreshed, allowing for the rotation grace period.</summary>
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
