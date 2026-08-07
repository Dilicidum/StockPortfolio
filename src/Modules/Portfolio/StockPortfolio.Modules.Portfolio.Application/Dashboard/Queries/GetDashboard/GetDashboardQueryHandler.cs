using System.Collections.ObjectModel;

using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Dashboard.Queries.GetDashboard;

public sealed class GetDashboardQueryHandler(
    IDashboardHoldingReader holdings,
    IQuoteReader quotes,
    ICompanyNameReader names,
    TimeProvider clock)
    : IQueryHandler<GetDashboardQuery, GetDashboardResult>
{
    public async Task<GetDashboardResult> Handle(GetDashboardQuery query, CancellationToken ct)
    {
        var rows = await holdings.GetVisibleHoldingsAsync(query.UserId, ct);

        if (rows.Count == 0)
        {
            return DashboardCalculator.Calculate(
                rows,
                ReadOnlyDictionary<string, QuotedPrice>.Empty,
                ReadOnlyDictionary<string, string>.Empty,
                clock.GetUtcNow());
        }

        string[] tickers = [.. rows.Select(row => row.Ticker)];

        var prices = await quotes.GetCurrentPricesAsync(query.UserId, tickers, ct);

        var known = await names.GetNamesAsync(tickers, ct);

        // Clock read after the fetch, so asOf is never earlier than the observedAt it is compared against.
        return DashboardCalculator.Calculate(rows, prices, known, clock.GetUtcNow());
    }
}
