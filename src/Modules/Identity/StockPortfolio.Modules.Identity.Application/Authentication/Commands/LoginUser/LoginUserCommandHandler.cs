using System.Diagnostics.CodeAnalysis;
using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;

/// <summary>Verifies a password and opens a session.</summary>
public sealed class LoginUserCommandHandler(
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<LoginUserCommand, LoginUserResult>
{
    /// <inheritdoc/>
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Must reproduce exactly the lower-cased canonical form User.Create persisted, because that string is the lookup key of the unique index.")]
    public async Task<LoginUserResult> Handle(LoginUserCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalisedEmail = (command.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(normalisedEmail, ct).ConfigureAwait(false);

        if (user is null)
        {
            // Verify against a fixed hash of nothing rather than returning here.
            _ = passwordHasher.Verify(command.Password, passwordHasher.DummyHash);
            return new InvalidCredentials();
        }

        if (!passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            return new InvalidCredentials();
        }

        var now = clock.GetUtcNow();
        var accessExpiresAt = now + TokenPolicy.AccessTokenLifetime;
        var accessToken = tokenIssuer.IssueAccessToken(user.Id, user.Email, accessExpiresAt);
        var refreshToken = tokenIssuer.NewRefreshToken();

        var session = RefreshToken.Issue(
            user.Id,
            tokenIssuer.HashRefreshToken(refreshToken),
            now + TokenPolicy.RefreshTokenLifetime,
            clock);

        await refreshTokens.AddAsync(session, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return new TokenPair(accessToken, refreshToken, accessExpiresAt);
    }
}
