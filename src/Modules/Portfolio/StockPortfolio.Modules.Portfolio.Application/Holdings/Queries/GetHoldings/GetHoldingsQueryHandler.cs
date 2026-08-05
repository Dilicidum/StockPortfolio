using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Queries.GetHoldings;

/// <summary>Lists a user's positions, naming the ones whose names happen to be cached.</summary>
public sealed class GetHoldingsQueryHandler(IHoldingRepository holdings, ICompanyNameReader names)
    : IQueryHandler<GetHoldingsQuery, IReadOnlyList<HoldingSummary>>
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<HoldingSummary>> Handle(GetHoldingsQuery query, CancellationToken ct)
    {
        var owned = await holdings.ListAsync(query.UserId, ct);
        var summaries = owned.Select(HoldingSummary.From).ToArray();

        if (summaries.Length == 0)
        {
            return summaries;
        }

        // Cache only, one batch, and never the provider: this page has no price column precisely so that
        // it never depends on the provider being up, and a cosmetic field must not give it one.
        var known = await names.GetNamesAsync([.. summaries.Select(summary => summary.Ticker)], ct);

        for (var index = 0; index < summaries.Length; index++)
        {
            if (known.TryGetValue(summaries[index].Ticker, out var name))
            {
                summaries[index] = summaries[index] with { Name = name };
            }
        }

        return summaries;
    }
}
