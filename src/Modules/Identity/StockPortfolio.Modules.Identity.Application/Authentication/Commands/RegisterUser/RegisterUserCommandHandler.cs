using OneOf;
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
    TimeProvider clock) : ICommandHandler<RegisterUserCommand, OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>>
{
    /// <inheritdoc/>
    public async Task<OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>> Handle(
        RegisterUserCommand command,
        CancellationToken ct)
    {
        // Hash first, unconditionally.
        var passwordHash = passwordHasher.Hash(command.Password);

        return await User.Create(command.Email, passwordHash, clock)
            .Match(
                user => AddThenIssueAsync(user, ct),
                invalid => Task.FromResult<OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>>(invalid));
    }

    private async Task<OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>> AddThenIssueAsync(
        User user,
        CancellationToken ct)
    {
        // No pre-SELECT for a duplicate address: two concurrent registrations would both see nothing and both insert.
        var outcome = await users.AddAsync(user, ct);

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

        await refreshTokens.AddAsync(session, ct);

        return new TokenPair(accessToken, refreshToken, accessExpiresAt);
    }
}
