using System.Text.Json.Serialization;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>Finnhub's /quote body. All seven numbers are optional — their schema declares no required list.</summary>
internal sealed record FinnhubQuoteResponse(
    [property: JsonPropertyName("c")] decimal? C,
    [property: JsonPropertyName("h")] decimal? H,
    [property: JsonPropertyName("l")] decimal? L,
    [property: JsonPropertyName("o")] decimal? O,
    [property: JsonPropertyName("pc")] decimal? Pc,
    [property: JsonPropertyName("d")] decimal? D,
    [property: JsonPropertyName("dp")] decimal? Dp,
    [property: JsonPropertyName("t")] long? T)
{
    /// <summary>Twelve digits or more is a millisecond stamp; ten is seconds.</summary>
    private const long MillisecondMagnitude = 100_000_000_000L;

    /// <summary>The usable price, or null. A missing or all-zero body is no price this cycle, not a bad symbol.</summary>
    public decimal? Price => C is { } c && c > 0m ? c : null;

    /// <summary>Finnhub's last-TRADE time, magnitude-guarded. Deliberately never a Quote's ObservedAt.</summary>
    public DateTimeOffset? TradeTimeUtc => T switch
    {
        null or <= 0 => null,
        >= MillisecondMagnitude => DateTimeOffset.FromUnixTimeMilliseconds(T.Value),
        _ => DateTimeOffset.FromUnixTimeSeconds(T.Value),
    };
}
