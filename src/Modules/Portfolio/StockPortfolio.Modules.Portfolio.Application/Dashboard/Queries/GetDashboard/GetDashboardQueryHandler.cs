using System.Collections.ObjectModel;

using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboard;

/// <summary>Joins the caller's positions to prices. No OneOf: an empty portfolio is a valid dashboard.</summary>
public sealed class GetDashboardQueryHandler(
    IDashboardHoldingReader holdings,
    IQuoteReader quotes,
    ICompanyNameReader names,
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
                ReadOnlyDictionary<string, string>.Empty,
                clock.GetUtcNow());
        }

        string[] tickers = [.. rows.Select(row => row.Ticker)];

        var prices = await quotes.GetCurrentPricesAsync(query.UserId, tickers, ct);

        // After the prices, and cache-only: a name is cosmetic, so it must never delay or fail the figures.
        var known = await names.GetNamesAsync(tickers, ct);

        // Read after the fetch, so asOf is never earlier than the observedAt it is compared against.
        return DashboardCalculator.Calculate(rows, prices, known, clock.GetUtcNow());
    }
}
