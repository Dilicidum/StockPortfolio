using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Modules.Portfolio.Application.Abstractions;

public sealed record HoldingRow(Guid Id, string Ticker, decimal Quantity, Money AveragePrice);

public interface IDashboardHoldingReader
{
    Task<IReadOnlyList<HoldingRow>> GetVisibleHoldingsAsync(Guid userId, CancellationToken ct);
}
