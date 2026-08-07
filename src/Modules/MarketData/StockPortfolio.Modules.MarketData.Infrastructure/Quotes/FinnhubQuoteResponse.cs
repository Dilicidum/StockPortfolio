using System.Text.Json.Serialization;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

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
    private const long MillisecondMagnitude = 100_000_000_000L;

    public decimal? Price => C is { } c && c > 0m ? c : null;

    public DateTimeOffset? TradeTimeUtc => T switch
    {
        null or <= 0 => null,
        >= MillisecondMagnitude => DateTimeOffset.FromUnixTimeMilliseconds(T.Value),
        _ => DateTimeOffset.FromUnixTimeSeconds(T.Value),
    };
}
