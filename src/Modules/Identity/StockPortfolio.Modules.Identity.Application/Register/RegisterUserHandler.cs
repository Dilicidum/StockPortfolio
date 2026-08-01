using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Register;

/// <summary>
/// Creates an account and opens the first session for it.
/// </summary>
/// <param name="passwordHasher">Hashes the password before it reaches the database.</param>
/// <param name="tokenIssuer">Mints the access and refresh tokens.</param>
/// <param name="users">Stores the new user.</param>
/// <param name="refreshTokens">Stores the new session.</param>
/// <param name="unitOfWork">Commits both.</param>
/// <param name="clock">Supplies every timestamp. Never <c>DateTimeOffset.UtcNow</c>.</param>
public sealed class RegisterUserHandler(
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RegisterUser, RegisterResult>
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public async Task<RegisterResult> Handle(RegisterUser command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Hash first, unconditionally. Hashing before knowing whether the address is free keeps
        // registration's cost independent of whether the account already exists.
        var passwordHash = passwordHasher.Hash(command.Password);

        return await User.Create(command.Email, passwordHash, clock)
            .Match(
                user => AddThenIssueAsync(user, ct),
                failure => Task.FromResult<RegisterResult>(failure))
            .ConfigureAwait(false);
    }

    private async Task<RegisterResult> AddThenIssueAsync(User user, CancellationToken ct)
    {
        // No pre-SELECT for a duplicate address: two concurrent registrations would both see
        // nothing and both proceed. The unique index is the check, and the repository turns its
        // 23505 into an outcome so this project never sees the driver.
        var outcome = await users.AddAsync(user, ct).ConfigureAwait(false);

        if (outcome is AddUserOutcome.EmailTaken)
        {
            return new EmailAlreadyUsed();
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
