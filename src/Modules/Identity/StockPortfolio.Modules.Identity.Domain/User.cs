using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using OneOf;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Domain;

/// <summary>
/// A person who can sign in. Owns nothing but an email address and a password hash — the
/// application deliberately stores no profile.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is exactly one constructor and it takes every mapped value.</b> It is private, so the
/// only route to a new <see cref="User"/> is <see cref="Create"/>, and there is no object
/// initialiser, no parameterless constructor and no public setter anywhere. A half-built
/// <see cref="User"/> is not representable.
/// </para>
/// <para>
/// EF Core's constructor binder matches on parameter name and will select this constructor for
/// materialisation. That is intended and safe <b>because the constructor only assigns</b>. The trap
/// worth knowing is the other arrangement: put the guards inside the constructor and EF re-runs
/// every one of them on every row of every <c>SELECT</c>. Validation therefore lives in
/// <see cref="Create"/>, which EF never calls.
/// </para>
/// </remarks>
public sealed class User
{
    /// <summary>The longest address the RFC 5321 forward path allows.</summary>
    private const int MaxEmailLength = 254;

    /// <summary>
    /// The only constructor. Assigns and nothing else — see the note on the class about why no
    /// guard may ever be added here.
    /// </summary>
    /// <param name="id">The identity of the user.</param>
    /// <param name="email">The already-normalised sign-in address.</param>
    /// <param name="passwordHash">The already-hashed password.</param>
    /// <param name="createdAt">When the account was created.</param>
    private User(UserId id, string email, string passwordHash, DateTimeOffset createdAt)
    {
        Id = id;
        Email = email;
        PasswordHash = passwordHash;
        CreatedAt = createdAt;
    }

    /// <summary>Gets the identity of the user. A UUIDv7, generated in the domain.</summary>
    public UserId Id { get; private set; }

    /// <summary>Gets the sign-in address, always trimmed and lower-cased.</summary>
    public string Email { get; private set; }

    /// <summary>Gets the PHC-encoded Argon2id hash of the password. Never the password itself.</summary>
    public string PasswordHash { get; private set; }

    /// <summary>Gets the instant the account was created, taken from the caller's clock.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a user, normalising the email and rejecting a malformed one. The only way to make one.
    /// </summary>
    /// <param name="email">The address as the caller typed it. Trimmed and lower-cased on the way in.</param>
    /// <param name="passwordHash">An already-hashed password. Hashing is the application layer's job.</param>
    /// <param name="clock">The clock supplying <see cref="CreatedAt"/>. Never <c>DateTimeOffset.UtcNow</c>.</param>
    /// <returns>
    /// The new user, or a <see cref="ValidationFailed"/> naming the <c>email</c> field when the
    /// address is not well formed.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="passwordHash"/> is null, empty or blank.</exception>
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Lower case is the stored canonical form of an email address and the key of the unique index; upper-casing would change what is persisted and looked up.")]
    public static OneOf<User, ValidationFailed> Create(string email, string passwordHash, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        if (string.IsNullOrWhiteSpace(email))
        {
            return new ValidationFailed("email", "Email is required.");
        }

        var normalised = email.Trim().ToLowerInvariant();

        if (!IsWellFormedEmail(normalised))
        {
            return new ValidationFailed("email", "Not a valid email address.");
        }

        return new User(UserId.New(), normalised, passwordHash, clock.GetUtcNow());
    }

    /// <summary>Replaces the stored password hash.</summary>
    /// <param name="newHash">The new PHC-encoded hash.</param>
    /// <exception cref="ArgumentException"><paramref name="newHash"/> is null, empty or blank.</exception>
    /// <remarks>
    /// An invariant, so it throws rather than returning a result case: no caller can legitimately
    /// ask for a blank password hash, and a silent empty hash would let anything authenticate.
    /// </remarks>
    public void ChangePasswordHash(string newHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newHash);
        PasswordHash = newHash;
    }

    /// <summary>
    /// Decides whether an already-normalised address is well formed enough to store.
    /// </summary>
    /// <param name="candidate">The trimmed, lower-cased address.</param>
    /// <returns><see langword="true"/> when the address is acceptable.</returns>
    /// <remarks>
    /// Deliberately structural rather than exhaustive. Full RFC 5322 conformance is neither
    /// achievable with a predicate nor useful — the only proof an address exists is a message sent
    /// to it. The user-facing shape rules live in FluentValidation on the command; this is the last
    /// line, so it stays cheap and total.
    /// </remarks>
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

        // TryCreate also accepts the display-name form ("Ann <a@b.com>"); comparing the parsed
        // address back to the input rejects anything that is not a bare address.
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
