using System.Text.Json.Serialization;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>Finnhub's /search body. The endpoint is fuzzy, so count is never the answer to "does this exist".</summary>
internal sealed record FinnhubSearchResponse(
    [property: JsonPropertyName("count")] int? Count,
    [property: JsonPropertyName("result")] IReadOnlyList<FinnhubSearchMatch>? Result)
{
    /// <summary>Whether a returned row IS this symbol: q=AAP returns AAPL, so a hit is not a match.</summary>
    public bool Contains(string ticker) =>
        Result?.Any(match => string.Equals(match.Symbol, ticker, StringComparison.OrdinalIgnoreCase)) == true;
}

/// <summary>One row of a /search result. Only the symbol decides existence.</summary>
internal sealed record FinnhubSearchMatch([property: JsonPropertyName("symbol")] string? Symbol);
