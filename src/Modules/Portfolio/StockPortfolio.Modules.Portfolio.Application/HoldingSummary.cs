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
    DateTimeOffset UpdatedAt)
{
    /// <summary>Projects a holding, computing what it cost.</summary>
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
            holding.UpdatedAt);
    }
}
