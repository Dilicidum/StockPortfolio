using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;
using OneOf.Types;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;

/// <summary>Closes a session so its refresh token can never be used again.</summary>
public sealed class RevokeSessionCommandHandler(
    ITokenIssuer tokenIssuer,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RevokeSessionCommand, RevokeSessionResult>
{
    /// <inheritdoc/>
    public async Task<RevokeSessionResult> Handle(RevokeSessionCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var presentedHash = tokenIssuer.HashRefreshToken(command.RefreshToken);
        var session = await refreshTokens.FindByHashAsync(presentedHash, ct).ConfigureAwait(false);

        // An already-closed session is reported the same as an unknown one: there is nothing left to revoke.
        if (session is null || session.SupersededAt is not null)
        {
            return new NotFound();
        }

        session.Revoke(clock);

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return new Success();
    }
}
