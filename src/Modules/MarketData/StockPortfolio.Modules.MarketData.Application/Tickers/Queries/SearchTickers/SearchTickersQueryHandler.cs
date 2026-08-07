using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.MarketData.Application.Tickers.Queries.SearchTickers;

public sealed class SearchTickersQueryHandler(IQuoteProvider provider, ICompanyNameStore names)
    : IQueryHandler<SearchTickersQuery, IReadOnlyList<SearchTickersResult>>
{
    public const int MinimumQueryLength = 2;

    public const int MaximumSuggestions = 20;

    public async Task<IReadOnlyList<SearchTickersResult>> Handle(SearchTickersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var trimmed = (query.Query ?? string.Empty).Trim();

        if (trimmed.Length < MinimumQueryLength)
        {
            return [];
        }

        var matches = await provider.SearchSymbolsAsync(trimmed, ct);

        if (matches.Count == 0)
        {
            return [];
        }

        await names.WriteAsync(matches, ct);

        return
        [
            .. matches
                .Take(MaximumSuggestions)
                .Select(match => new SearchTickersResult(match.Ticker.Value, match.Name)),
        ];
    }
}
