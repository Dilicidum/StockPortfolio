using System.Diagnostics.CodeAnalysis;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Identity.Domain;

/// <summary>
/// One login session. The refresh token itself is 32 random bytes that leave the server exactly
/// once; only its SHA-256 hash is stored, so a database leak yields nothing replayable.
/// </summary>
/// <remarks>
/// <para>
/// A session ends in one of three ways, all expressed with the properties below: it expires
/// (<see cref="ExpiresAt"/> passes), it is rotated (<see cref="Supersede"/> links the replacement),
/// or it is revoked at logout (<see cref="Revoke"/> — superseded, with no replacement, which is
/// why <see cref="SupersededBy"/> is nullable).
/// </para>
/// <para>
/// As with <see cref="User"/>: <c>Id</c> is not re-declared, there is no constructor whose
/// parameter names match mapped properties, and no setter validates anything.
/// </para>
/// </remarks>
public sealed class RefreshToken : AggregateRoot<RefreshTokenId>
{
    /// <summary>EF Core materialisation only. Runs no validation and sets nothing.</summary>
    private RefreshToken()
    {
    }

    /// <summary>Gets the user this session belongs to.</summary>
    public UserId UserId { get; private set; }

    /// <summary>Gets the SHA-256 hash of the token string handed to the client.</summary>
    /// <remarks>
    /// SHA-256 with no work factor is correct here precisely <i>because</i> the input is already a
    /// uniformly random 256-bit value. Argon2 over it would buy no extra brute-force resistance and
    /// would cost 19 MiB of memory on every refresh.
    /// </remarks>
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "A fixed-length SHA-256 digest mapped straight onto a bytea column. Wrapping it would force a value converter and a second type for no behavioural gain, and EF Core must be able to read and write the property directly.")]
    public byte[] TokenHash { get; private set; } = null!;

    /// <summary>Gets the instant after which the session can no longer be refreshed.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Gets the instant the session was opened.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Gets the instant the session was rotated or revoked, or <see langword="null"/> while it is
    /// still the current session.
    /// </summary>
    public DateTimeOffset? SupersededAt { get; private set; }

    /// <summary>
    /// Gets the session that replaced this one, or <see langword="null"/> when it was revoked
    /// outright rather than rotated.
    /// </summary>
    /// <remarks>
    /// Keeping the link is what makes replay detectable: a request presenting a token that is
    /// already superseded is either a stale tab or a stolen token, and the chain says which
    /// session to kill.
    /// </remarks>
    public RefreshTokenId? SupersededBy { get; private set; }

    /// <summary>Opens a session.</summary>
    /// <param name="userId">The user signing in.</param>
    /// <param name="tokenHash">SHA-256 of the token string handed to the client.</param>
    /// <param name="expiresAt">When the session stops being refreshable.</param>
    /// <param name="clock">The clock supplying <see cref="CreatedAt"/>.</param>
    /// <returns>The new session.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="tokenHash"/> or <paramref name="clock"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="tokenHash"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="expiresAt"/> is not in the future.
    /// </exception>
    public static RefreshToken Issue(
        UserId userId,
        byte[] tokenHash,
        DateTimeOffset expiresAt,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        ArgumentNullException.ThrowIfNull(clock);

        if (tokenHash.Length == 0)
        {
            throw new ArgumentException("A refresh token hash cannot be empty.", nameof(tokenHash));
        }

        var now = clock.GetUtcNow();

        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAt),
                expiresAt,
                "A refresh token cannot be issued already expired.");
        }

        return new RefreshToken
        {
            Id = RefreshTokenId.New(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Reports whether the session can still be exchanged for a new token pair.
    /// </summary>
    /// <param name="clock">The clock to read the current instant from.</param>
    /// <returns>
    /// <see langword="true"/> when the session has neither been superseded nor expired.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Strictly the current session. The rotation grace period — during which a just-superseded
    /// token is still honoured so two open tabs do not log each other out — is a policy decision and
    /// lives in the application layer, not here.
    /// </remarks>
    public bool IsActive(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return SupersededAt is null && clock.GetUtcNow() < ExpiresAt;
    }

    /// <summary>Rotates this session, handing its role to <paramref name="replacement"/>.</summary>
    /// <param name="replacement">The session issued in its place.</param>
    /// <param name="clock">The clock supplying <see cref="SupersededAt"/>.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="replacement"/> or <paramref name="clock"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="replacement"/> is this session, or belongs to a different user.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The session has already been superseded or revoked. This is an invariant, not a context
    /// failure: superseding twice would silently overwrite the first replacement link and destroy
    /// the audit chain replay detection depends on. Callers that expect it must check
    /// <see cref="SupersededAt"/> first.
    /// </exception>
    public void Supersede(RefreshToken replacement, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(clock);

        if (replacement.Id.Equals(Id))
        {
            throw new ArgumentException(
                "A refresh token cannot supersede itself.",
                nameof(replacement));
        }

        if (!replacement.UserId.Equals(UserId))
        {
            throw new ArgumentException(
                "A refresh token can only be superseded by one issued to the same user.",
                nameof(replacement));
        }

        EnsureNotAlreadySuperseded();

        SupersededAt = clock.GetUtcNow();
        SupersededBy = replacement.Id;
    }

    /// <summary>Ends this session with no replacement — the logout path.</summary>
    /// <param name="clock">The clock supplying <see cref="SupersededAt"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The session has already been superseded or revoked.</exception>
    public void Revoke(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        EnsureNotAlreadySuperseded();

        SupersededAt = clock.GetUtcNow();
        SupersededBy = null;
    }

    private void EnsureNotAlreadySuperseded()
    {
        if (SupersededAt is not null)
        {
            throw new InvalidOperationException(
                "This refresh token was already superseded at "
                + SupersededAt.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                + ".");
        }
    }
}
