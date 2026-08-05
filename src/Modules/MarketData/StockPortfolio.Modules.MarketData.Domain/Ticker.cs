using System.Text.RegularExpressions;
using OneOf;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.MarketData.Domain;

/// <summary>A stock symbol, upper-cased and shape-checked. MarketData's own; it meets Portfolio's as a string.</summary>
public readonly partial record struct Ticker(string Value)
{
    /// <summary>The longest symbol the shape allows.</summary>
    public const int MaxLength = 5;

    /// <summary>Creates a ticker, trimming and upper-casing first.</summary>
    public static OneOf<Ticker, InvalidInput> Create(string? candidate)
    {
        var normalised = (candidate ?? string.Empty).Trim().ToUpperInvariant();

        return Shape().IsMatch(normalised)
            ? new Ticker(normalised)
            : new InvalidInput("ticker", $"A ticker is 1 to {MaxLength} letters, A to Z.");
    }

    /// <summary>Returns the normalised symbol itself.</summary>
    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z]{1,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
