using OneOf;
using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.LoginUser;

/// <summary>Verifies a password and opens a session.</summary>
public sealed class LoginUserCommandHandler(
    IPasswordHasher passwordHasher,
    IUserRepository users,
    SessionOpener sessions) : ICommandHandler<LoginUserCommand, OneOf<TokenPair, InvalidCredentials>>
{
    /// <inheritdoc/>
    public async Task<OneOf<TokenPair, InvalidCredentials>> Handle(LoginUserCommand command, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(User.NormaliseEmail(command.Email), ct);

        if (user is null)
        {
            // Verify against a fixed hash of nothing rather than returning here, so both replies take the same time.
            passwordHasher.Verify(command.Password, passwordHasher.DummyHash);
            return new InvalidCredentials();
        }

        if (!passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            return new InvalidCredentials();
        }

        return await sessions.OpenAsync(user, ct);
    }
}
