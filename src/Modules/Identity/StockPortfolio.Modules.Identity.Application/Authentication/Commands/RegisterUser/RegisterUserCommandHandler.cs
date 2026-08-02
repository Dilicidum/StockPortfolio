using OneOf;
using StockPortfolio.Modules.Identity.Application.Abstractions;
using StockPortfolio.Modules.Identity.Domain;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Application.Authentication.Commands.RegisterUser;

/// <summary>Creates an account and opens the first session for it.</summary>
public sealed class RegisterUserCommandHandler(
    IPasswordHasher passwordHasher,
    IUserRepository users,
    SessionOpener sessions,
    TimeProvider clock) : ICommandHandler<RegisterUserCommand, OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>>
{
    /// <inheritdoc/>
    public async Task<OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>> Handle(
        RegisterUserCommand command,
        CancellationToken ct)
    {
        // Asked before the password is hashed: Argon2id is deliberately slow, and a taken address is a
        // 409 whatever the password was.
        var taken = await users.FindByEmailAsync(User.NormaliseEmail(command.Email), ct);

        if (taken is not null)
        {
            return new EmailAlreadyUsed();
        }

        var passwordHash = passwordHasher.Hash(command.Password);

        return await User.Create(command.Email, passwordHash, clock)
            .Match(
                user => AddThenOpenAsync(user, ct),
                invalid => Task.FromResult<OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>>(invalid));
    }

    private async Task<OneOf<TokenPair, EmailAlreadyUsed, InvalidInput>> AddThenOpenAsync(
        User user,
        CancellationToken ct)
    {
        await users.AddAsync(user, ct);

        return await sessions.OpenAsync(user, ct);
    }
}
