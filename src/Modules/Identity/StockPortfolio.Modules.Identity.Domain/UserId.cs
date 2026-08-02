using System.Globalization;

namespace StockPortfolio.Modules.Identity.Domain;

/// <summary>The identity of a User.</summary>
public readonly record struct UserId(Guid Value)
{
    /// <summary>Creates a new, time-ordered user id.</summary>
    public static UserId New() => new(Guid.CreateVersion7());

    /// <summary>Returns the canonical hyphenated form of the underlying value.</summary>
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
