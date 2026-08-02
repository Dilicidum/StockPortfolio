using System.Globalization;

namespace StockPortfolio.Modules.Identity.Domain;

/// <summary>The identity of a RefreshToken — that is, of one login session.</summary>
public readonly record struct RefreshTokenId(Guid Value)
{
    /// <summary>Creates a new, time-ordered refresh-token id.</summary>
    public static RefreshTokenId New() => new(Guid.CreateVersion7());

    /// <summary>Returns the canonical hyphenated form of the underlying value.</summary>
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
