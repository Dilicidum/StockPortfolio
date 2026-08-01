using System.Diagnostics.CodeAnalysis;
using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Login;

/// <summary>
/// Verifies a password and opens a session.
/// </summary>
/// <param name="passwordHasher">Verifies the password — and burns the same time when there is no account.</param>
/// <param name="tokenIssuer">Mints the access and refresh tokens.</param>
/// <param name="users">Finds the account.</param>
/// <param name="refreshTokens">Stores the new session.</param>
/// <param name="unitOfWork">Commits it.</param>
/// <param name="clock">Supplies every timestamp.</param>
public sealed class LoginUserHandler(
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<LoginUser, LoginResult>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Must reproduce exactly the lower-cased canonical form User.Create persisted, because that string is the lookup key of the unique index.")]
    public async Task<LoginResult> Handle(LoginUser command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalisedEmail = (command.Email ?? string.Empty).Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(normalisedEmail, ct).ConfigureAwait(false);

        if (user is null)
        {
            // Verify against a fixed hash of nothing rather than returning here. An early return
            // makes an unknown address answer in microseconds and a known one in the tens of
            // milliseconds Argon2 costs, and that difference is an account-enumeration oracle
            // whether or not the response bodies are identical.
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
