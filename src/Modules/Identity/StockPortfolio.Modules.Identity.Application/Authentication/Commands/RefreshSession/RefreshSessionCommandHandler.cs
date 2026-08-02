using OneOf;
using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RefreshSession;

/// <summary>Exchanges a refresh token for a new token pair, rotating the presented session into its replacement.</summary>
public sealed class RefreshSessionCommandHandler(
    ITokenIssuer tokenIssuer,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    SessionOpener sessions,
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

        return await sessions.RotateAsync(user, session, ct);
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
        if (session.SupersededBy is null || session.SupersededAt is not { } supersededAt)
        {
            return false;
        }

        var now = clock.GetUtcNow();

        return now < session.ExpiresAt && now - supersededAt <= TokenPolicy.RotationGracePeriod;
    }
}
