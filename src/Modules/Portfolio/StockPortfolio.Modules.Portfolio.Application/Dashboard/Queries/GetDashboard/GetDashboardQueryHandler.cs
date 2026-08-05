using System.Collections.ObjectModel;

using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboard;

/// <summary>Joins the caller's positions to prices. No OneOf: an empty portfolio is a valid dashboard.</summary>
public sealed class GetDashboardQueryHandler(
    IDashboardHoldingReader holdings,
    IQuoteReader quotes,
    TimeProvider clock)
    : IQueryHandler<GetDashboardQuery, GetDashboardResult>
{
    /// <inheritdoc/>
    public async Task<GetDashboardResult> Handle(GetDashboardQuery query, CancellationToken ct)
    {
        var rows = await holdings.GetVisibleHoldingsAsync(query.UserId, ct);

        if (rows.Count == 0)
        {
            // Short-circuits before MarketData: otherwise a brand-new account pays a Redis round trip
            // and a provider call on every dashboard poll, forever.
            return DashboardCalculator.Calculate(
                rows,
                ReadOnlyDictionary<string, QuotedPrice>.Empty,
                clock.GetUtcNow());
        }

        var prices = await quotes.GetCurrentPricesAsync([.. rows.Select(row => row.Ticker)], ct);

        // Read after the fetch, so asOf is never earlier than the observedAt it is compared against.
        return DashboardCalculator.Calculate(rows, prices, clock.GetUtcNow());
    }
}
