using System.Text.RegularExpressions;
using OneOf;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.MarketData.Domain;

public readonly partial record struct Ticker(string Value)
{
    public const int MaxLength = 5;

    public static Ticker? TryParse(string? candidate) =>
        Create(candidate).Match(parsed => (Ticker?)parsed, badTicker => null);

    public static OneOf<Ticker, InvalidInput> Create(string? candidate)
    {
        var normalised = (candidate ?? string.Empty).Trim().ToUpperInvariant();

        return Shape().IsMatch(normalised)
            ? new Ticker(normalised)
            : new InvalidInput("ticker", $"A ticker is 1 to {MaxLength} letters, A to Z.");
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z]{1,5}$", RegexOptions.CultureInvariant)]
    private static partial Regex Shape();
}
