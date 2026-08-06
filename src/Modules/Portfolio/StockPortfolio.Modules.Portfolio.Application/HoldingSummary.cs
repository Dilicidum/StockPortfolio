using System.Text.Json.Serialization;

using StockPortfolio.Modules.Portfolio.Domain;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Application;

/// <summary>One position as the client sees it. Money is computed here, never in the browser.</summary>
public sealed record HoldingSummary(
    Guid Id,
    string Ticker,
    decimal Quantity,
    Money AveragePrice,
    Money Invested,
    bool IsVisible,
    DateTimeOffset UpdatedAt,

    // JsonIgnoreCondition.Never because Program.cs sets DefaultIgnoreCondition to WhenWritingNull, which
    // would drop the member entirely — and no client can tell an absent member from a null one. Null here
    // means "no name is cached", which is the ordinary case for a position added before names existed.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Name)
{
    /// <summary>Projects a holding, computing what it cost. The name is filled in afterwards, or not at all.</summary>
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
