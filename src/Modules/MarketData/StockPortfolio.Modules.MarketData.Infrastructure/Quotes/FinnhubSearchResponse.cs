using System.Text.Json.Serialization;

using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

internal sealed record FinnhubSearchResponse(
    [property: JsonPropertyName("count")] int? Count,
    [property: JsonPropertyName("result")] IReadOnlyList<FinnhubSearchMatch>? Result)
{
    public bool Contains(string ticker) =>
        Result?.Any(match => string.Equals(match.Symbol, ticker, StringComparison.OrdinalIgnoreCase)) == true;

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

internal sealed record FinnhubSearchMatch(
    [property: JsonPropertyName("symbol")] string? Symbol,
    [property: JsonPropertyName("description")] string? Description);
