using System.Text.Json.Serialization;

using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Application;

public sealed record HoldingSummary(
    Guid Id,
    string Ticker,
    decimal Quantity,
    Money AveragePrice,
    Money Invested,
    bool IsVisible,
    DateTimeOffset UpdatedAt,

    // JsonIgnoreCondition.Never: the host's WhenWritingNull default would drop the member entirely.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Name)
{
    public static HoldingSummary From(Holding holding)
    {
        ArgumentNullException.ThrowIfNull(holding);

        return new HoldingSummary(
            holding.Id.Value,
            holding.Ticker.Value,
            holding.Quantity,
            holding.AveragePrice,
            holding.AveragePrice.Multiply(holding.Quantity),
            holding.IsVisible,
            holding.UpdatedAt,
            Name: null);
    }
}
