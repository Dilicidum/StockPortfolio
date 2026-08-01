using System.Globalization;

namespace StockPortfolio.Modules.Identity.Domain;

/// <summary>
/// The identity of a <see cref="RefreshToken"/> — that is, of one login session.
/// </summary>
/// <param name="Value">The underlying database value.</param>
/// <remarks>
/// UUIDv7 for the same index-locality reason as <see cref="UserId"/>: refresh tokens are written
/// on every login and every rotation, so this is the hottest insert path in the module.
/// </remarks>
public readonly record struct RefreshTokenId(Guid Value)
{
    /// <summary>Creates a new, time-ordered refresh-token id.</summary>
    /// <returns>A <see cref="RefreshTokenId"/> wrapping a fresh UUIDv7.</returns>
    public static RefreshTokenId New() => new(Guid.CreateVersion7());

    /// <summary>Returns the canonical hyphenated form of the underlying value.</summary>
    /// <returns>The id as a string.</returns>
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
