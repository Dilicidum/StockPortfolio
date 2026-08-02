using OneOf;
using OneOf.Types;
using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RevokeSession;

/// <summary>Closes a session so its refresh token can never be used again.</summary>
public sealed class RevokeSessionCommandHandler(
    ITokenIssuer tokenIssuer,
    IRefreshTokenRepository refreshTokens,
    TimeProvider clock) : ICommandHandler<RevokeSessionCommand, OneOf<Success, NotFound>>
{
    /// <inheritdoc/>
    public async Task<OneOf<Success, NotFound>> Handle(RevokeSessionCommand command, CancellationToken ct)
    {
        var presentedHash = tokenIssuer.HashRefreshToken(command.RefreshToken);
        var session = await refreshTokens.FindByHashAsync(presentedHash, ct);

        // An already-closed session is reported the same as an unknown one: there is nothing left to revoke.
        if (session is null || session.SupersededAt is not null)
        {
            return new NotFound();
        }

        session.Revoke(clock);

        await refreshTokens.UpdateAsync(session, ct);

        return new Success();
    }
}
