using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

/// <summary>How lively the generated walk is. Both values are optional and neither can fail startup.</summary>
internal sealed class FakeQuoteOptions
{
    /// <summary>The configuration section these two values are read from.</summary>
    public const string SectionName = "MarketData:Fake";

    private FakeQuoteOptions(decimal volatilityPerMinute, decimal driftPerMinute)
    {
        VolatilityPerMinute = volatilityPerMinute;
        DriftPerMinute = driftPerMinute;
    }

    /// <summary>Half-width of each minute's multiplicative step.</summary>
    public decimal VolatilityPerMinute { get; }

    /// <summary>A constant added to every step, so a demo can be made to trend.</summary>
    public decimal DriftPerMinute { get; }

    /// <summary>Reads the section, falling back to the defaults for anything absent or unparseable.</summary>
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
