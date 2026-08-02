using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using OneOf;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Domain;

/// <summary>A person who can sign in.</summary>
public sealed class User
{
    /// <summary>The longest address the RFC 5321 forward path allows.</summary>
    private const int MaxEmailLength = 254;

    /// <summary>The only constructor.</summary>
    private User(UserId id, string email, string passwordHash, DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the identity of the user.</summary>
    public UserId Id { get; private set; }

    /// <summary>Gets the sign-in address, always trimmed and lower-cased.</summary>
    public string Email { get; private set; }

    /// <summary>Gets the PHC-encoded Argon2id hash of the password.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>Gets the instant the account was created, taken from the caller's clock.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Creates a user, normalising the email and rejecting a malformed one.</summary>
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Lower case is the stored canonical form of an email address and the key of the unique index; upper-casing would change what is persisted and looked up.")]
    public static OneOf<User, InvalidInput> Create(string email, string passwordHash, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (string.IsNullOrWhiteSpace(email))
        {
            return new InvalidInput("email", "Email is required.");
        }

        var normalised = email.Trim().ToLowerInvariant();

        if (!IsWellFormedEmail(normalised))
        {
            return new InvalidInput("email", "Not a valid email address.");
        }

        return new User(UserId.New(), normalised, passwordHash, clock.GetUtcNow());
    }

    /// <summary>Replaces the stored password hash.</summary>
    public void ChangePasswordHash(string newHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newHash);
        PasswordHash = newHash;
    }

    /// <summary>Decides whether an already-normalised address is well formed enough to store.</summary>
    private static bool IsWellFormedEmail(string candidate)
    {
        if (candidate.Length > MaxEmailLength)
        {
            return false;
        }

        if (candidate.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        // TryCreate also accepts the display-name form ("Ann <a@b.com>"); comparing the parsed address back.
        if (!MailAddress.TryCreate(candidate, out var parsed)
            || !string.Equals(parsed.Address, candidate, StringComparison.Ordinal))
        {
            return false;
        }

        var host = parsed.Host;

        return host.Contains('.', StringComparison.Ordinal)
            && !host.StartsWith('.')
            && !host.EndsWith('.');
    }
}
