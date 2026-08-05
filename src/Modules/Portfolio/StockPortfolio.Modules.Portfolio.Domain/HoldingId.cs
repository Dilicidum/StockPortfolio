using System.Globalization;

namespace StockPortfolio.Modules.Portfolio.Domain;

/// <summary>The identity of a Holding.</summary>
public readonly record struct HoldingId(Guid Value)
{
    /// <summary>Creates a new, time-ordered holding id.</summary>
    public static HoldingId New() => new(Guid.CreateVersion7());

    /// <summary>Returns the canonical hyphenated form of the underlying value.</summary>
    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
