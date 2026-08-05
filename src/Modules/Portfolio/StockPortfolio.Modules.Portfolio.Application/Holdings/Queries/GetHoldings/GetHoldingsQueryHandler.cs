using StockPortfolio.Modules.Portfolio.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.Portfolio.Application.Holdings.Queries.GetHoldings;

/// <summary>Lists a user's positions.</summary>
public sealed class GetHoldingsQueryHandler(IHoldingRepository holdings)
    : IQueryHandler<GetHoldingsQuery, IReadOnlyList<HoldingSummary>>
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<HoldingSummary>> Handle(GetHoldingsQuery query, CancellationToken ct)
    {
        var owned = await holdings.ListAsync(query.UserId, ct);

        return [.. owned.Select(HoldingSummary.From)];
    }
}
