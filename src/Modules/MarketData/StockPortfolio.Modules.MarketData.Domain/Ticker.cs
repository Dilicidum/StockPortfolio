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
    /// <summary>The parsed ticker, or null when the candidate is not one.</summary>
    /// <remarks>
    /// For callers that skip a bad ticker rather than report it — a filter, a lookup, a background
    /// sample. Create is the one to use anywhere the failure reaches a user, because this deliberately
    /// discards which rule was broken.
    /// </remarks>
    public static Ticker? TryParse(string? candidate) =>
        Create(candidate).Match(parsed => (Ticker?)parsed, badTicker => null);

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
