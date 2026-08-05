using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Application.Abstractions;

/// <summary>One position as the dashboard read returns it: no aggregate, nothing tracked.</summary>
public sealed record HoldingRow(Guid Id, string Ticker, decimal Quantity, Money AveragePrice);

/// <summary>The dashboard's read model, kept off IHoldingRepository because that one's reads are tracked.</summary>
public interface IDashboardHoldingReader
{
    /// <summary>Lists the positions the dashboard shows, newest first.</summary>
    Task<IReadOnlyList<HoldingRow>> GetVisibleHoldingsAsync(Guid userId, CancellationToken ct);
}
