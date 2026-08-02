using System.Diagnostics.CodeAnalysis;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Identity.Domain;

/// <summary>One login session.</summary>
public sealed class RefreshToken
{
    /// <summary>The only constructor.</summary>
    private RefreshToken(
        RefreshTokenId id,
        UserId userId,
        byte[] tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt,
        DateTimeOffset? supersededAt,
        RefreshTokenId? supersededBy)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = createdAt;
        SupersededAt = supersededAt;
        SupersededBy = supersededBy;
    }

    /// <summary>Gets the identity of the session.</summary>
    public RefreshTokenId Id { get; private set; }

    /// <summary>Gets the user this session belongs to.</summary>
    public UserId UserId { get; private set; }

    /// <summary>Gets the SHA-256 hash of the token string handed to the client.</summary>
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "A fixed-length SHA-256 digest mapped straight onto a bytea column. Wrapping it would force a value converter and a second type for no behavioural gain, and EF Core must be able to read and write the property directly.")]
    public byte[] TokenHash { get; private set; } = null!;

    /// <summary>Gets the instant after which the session can no longer be refreshed.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Gets the instant the session was opened.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Gets the instant the session was rotated or revoked, or null while it is still the current session.</summary>
    public DateTimeOffset? SupersededAt { get; private set; }

    /// <summary>Gets the session that replaced this one, or null when it was revoked outright rather than rotated.</summary>
    public RefreshTokenId? SupersededBy { get; private set; }

    /// <summary>Opens a session.</summary>
    public static RefreshToken Issue(
        UserId userId,
        byte[] tokenHash,
        DateTimeOffset expiresAt,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

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

        return new RefreshToken(
            RefreshTokenId.New(),
            userId,
            tokenHash,
            expiresAt,
            now,
            supersededAt: null,
            supersededBy: null);
    }

    /// <summary>Reports whether the session can still be exchanged for a new token pair.</summary>
    public bool IsActive(TimeProvider clock) =>
        SupersededAt is null && clock.GetUtcNow() < ExpiresAt;

    /// <summary>Rotates this session, handing its role to replacement.</summary>
    public void Supersede(RefreshToken replacement, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(replacement);

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
    public void Revoke(TimeProvider clock)
    {
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
