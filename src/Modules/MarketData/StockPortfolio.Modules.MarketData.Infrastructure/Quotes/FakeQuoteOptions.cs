using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

internal sealed class FakeQuoteOptions
{
    public const string SectionName = "MarketData:Fake";

    private FakeQuoteOptions(decimal volatilityPerMinute, decimal driftPerMinute)
    {
        VolatilityPerMinute = volatilityPerMinute;
        DriftPerMinute = driftPerMinute;
    }

    public decimal VolatilityPerMinute { get; }

    public decimal DriftPerMinute { get; }

    public static FakeQuoteOptions FromConfiguration(IConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var section = config.GetSection(SectionName);

        return new FakeQuoteOptions(
            Read(section["VolatilityPerMinute"], 0.002m),
            Read(section["DriftPerMinute"], 0m));
    }

    private static decimal Read(string? value, decimal fallback) =>
        decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
}
