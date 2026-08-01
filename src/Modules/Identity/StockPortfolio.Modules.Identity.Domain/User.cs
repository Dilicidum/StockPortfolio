using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using OneOf;
using StockPortfolio.Shared.Kernel;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Identity.Domain;

/// <summary>
/// A person who can sign in. Owns nothing but an email address and a password hash — the
/// application deliberately stores no profile, so this aggregate stays the whole of Identity.
/// </summary>
/// <remarks>
/// <para>
/// <c>Id</c> is <b>not</b> re-declared here. It is declared once on
/// <see cref="AggregateRoot{TId}"/>; a re-declaration is CS0108 (hides inherited member), which
/// <c>TreatWarningsAsErrors</c> turns into a build error. EF Core maps the inherited property
/// normally.
/// </para>
/// <para>
/// There is no constructor taking the mapped values. EF Core's constructor binder is
/// convention-based, matches on parameter name, and is blind to accessibility — a
/// <c>private User(UserId id, string email, …)</c> would be selected for materialisation and would
/// run the factory's guards on every <c>SELECT</c>. Construction goes through
/// <see cref="Create"/> and an object initialiser instead.
/// </para>
/// <para>
/// There is likewise no validation in the setters. <c>PropertyAccessMode.PreferField</c> has been
/// EF Core's default since 3.0, so EF writes the backing field and never calls the setter;
/// validation there is dead code that looks alive.
/// </para>
/// </remarks>
public sealed class User : AggregateRoot<UserId>
{
    /// <summary>The longest address the RFC 5321 forward path allows.</summary>
    private const int MaxEmailLength = 254;

    /// <summary>EF Core materialisation only. Runs no validation and sets nothing.</summary>
    private User()
    {
    }

    /// <summary>Gets the sign-in address, always trimmed and lower-cased.</summary>
    public string Email { get; private set; } = null!;

    /// <summary>Gets the PHC-encoded Argon2id hash of the password. Never the password itself.</summary>
    public string PasswordHash { get; private set; } = null!;

    /// <summary>Gets the instant the account was created, taken from the caller's clock.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Creates a user, normalising the email and rejecting a malformed one.
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

        return new User
        {
            Id = UserId.New(),
            Email = normalised,
            PasswordHash = passwordHash,
            CreatedAt = clock.GetUtcNow(),
        };
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
    /// to it. The user-facing shape rules (length, character classes) live in FluentValidation on
    /// the HTTP request; this is the last line, so it stays cheap and total.
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
