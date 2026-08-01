using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Revoke;

/// <summary>
/// Closes a session so its refresh token can never be used again.
/// </summary>
/// <param name="tokenIssuer">Hashes the presented token to find the session.</param>
/// <param name="refreshTokens">Finds the session.</param>
/// <param name="unitOfWork">Commits the revocation.</param>
/// <param name="clock">Supplies the revocation timestamp.</param>
/// <remarks>
/// Only the refresh token is revoked. The access token is self-contained and cannot be recalled, so
/// it stays valid until it expires — which is what makes
/// <see cref="TokenPolicy.AccessTokenLifetime"/> the real revocation latency of this system.
/// </remarks>
public sealed class RevokeSessionHandler(
    ITokenIssuer tokenIssuer,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RevokeSession, RevokeResult>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public async Task<RevokeResult> Handle(RevokeSession command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var presentedHash = tokenIssuer.HashRefreshToken(command.RefreshToken);
        var session = await refreshTokens.FindByHashAsync(presentedHash, ct).ConfigureAwait(false);

        // An already-closed session is reported the same as an unknown one: there is nothing left
        // to revoke either way, and the caller learns nothing about which tokens ever existed.
        if (session is null || session.SupersededAt is not null)
        {
            return new SessionNotFound();
        }

        session.Revoke(clock);

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return new Success();
    }
}
