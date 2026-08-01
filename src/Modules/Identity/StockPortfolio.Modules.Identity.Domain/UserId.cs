using System.Globalization;

namespace StockPortfolio.Modules.Identity.Domain;

/// <summary>
/// The identity of a <see cref="User"/>. A strongly-typed wrapper so a user id can never be
/// passed where a portfolio id or a refresh-token id is expected.
/// </summary>
/// <param name="Value">The underlying database value.</param>
/// <remarks>
/// <para>
/// <see cref="New"/> generates a UUIDv7, whose leading 48 bits are a millisecond timestamp, so
/// freshly inserted rows land at the right-hand edge of the primary-key index instead of being
/// scattered across it.
/// </para>
/// <para>
/// The id is generated <b>here</b>, in the domain, and mapped <c>ValueGeneratedNever()</c>.
/// Npgsql's own sequential-GUID generator selects on <c>property.ClrType</c>, which for this
/// property is <see cref="UserId"/> and not <see cref="Guid"/>, so it would never fire.
/// </para>
/// </remarks>
public readonly record struct UserId(Guid Value)
{
    /// <summary>Creates a new, time-ordered user id.</summary>
    /// <returns>A <see cref="UserId"/> wrapping a fresh UUIDv7.</returns>
    public static UserId New() => new(Guid.CreateVersion7());

    /// <summary>Returns the canonical hyphenated form of the underlying value.</summary>
    /// <returns>The id as a string.</returns>
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
