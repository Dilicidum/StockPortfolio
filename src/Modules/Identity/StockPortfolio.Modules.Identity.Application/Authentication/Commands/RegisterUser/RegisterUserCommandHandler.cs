using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;

/// <summary>Creates an account and opens the first session for it.</summary>
public sealed class RegisterUserCommandHandler(
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<RegisterUserCommand, RegisterUserResult>
{
    /// <inheritdoc/>
    public async Task<RegisterUserResult> Handle(RegisterUserCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Hash first, unconditionally.
        var passwordHash = passwordHasher.Hash(command.Password);

        return await User.Create(command.Email, passwordHash, clock)
            .Match(
                user => AddThenIssueAsync(user, ct),
                failure => Task.FromResult<RegisterUserResult>(failure))
            .ConfigureAwait(false);
    }

    private async Task<RegisterUserResult> AddThenIssueAsync(User user, CancellationToken ct)
    {
        // No pre-SELECT for a duplicate address: two concurrent registrations would both see nothing and both.
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
