using System.Text.Json.Serialization;

using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>Finnhub's /search body. The endpoint is fuzzy, so count is never the answer to "does this exist".</summary>
internal sealed record FinnhubSearchResponse(
    [property: JsonPropertyName("count")] int? Count,
    [property: JsonPropertyName("result")] IReadOnlyList<FinnhubSearchMatch>? Result)
{
    /// <summary>Whether a returned row IS this symbol: q=appl returns Applovin too, so a hit is not a match.</summary>
    public bool Contains(string ticker) =>
        Result?.Any(match => string.Equals(match.Symbol, ticker, StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>The rows a user could actually act on: /search also returns foreign listings such as
    /// AAPL.SW, and offering one would fill the add-position field with something the form then rejects.</summary>
    public IReadOnlyList<SymbolMatch> Suggestions()
    {
        if (Result is null)
        {
            return [];
        }

        var suggestions = new List<SymbolMatch>(Result.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in Result)
        {
            if (string.IsNullOrWhiteSpace(row.Description)
                || Ticker.TryParse(row.Symbol) is not { } ticker
                || !seen.Add(ticker.Value))
            {
                continue;
            }

            suggestions.Add(new SymbolMatch(ticker, row.Description.Trim()));
        }

        return suggestions;
    }
}

/// <summary>One row of a /search result. Only the symbol decides existence; the description is the company.</summary>
internal sealed record FinnhubSearchMatch(
    [property: JsonPropertyName("symbol")] string? Symbol,
    [property: JsonPropertyName("description")] string? Description);
