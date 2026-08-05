using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Modules.MarketData.Application.Tickers.Queries.SearchTickers;

/// <summary>Searches, and warms the name cache with everything it saw. No OneOf: a miss is an empty list.</summary>
public sealed class SearchTickersQueryHandler(IQuoteProvider provider, ICompanyNameStore names)
    : IQueryHandler<SearchTickersQuery, IReadOnlyList<SearchTickersResult>>
{
    /// <summary>One letter matches most of the market, so the first useful query is two.</summary>
    public const int MinimumQueryLength = 2;

    /// <summary>What a dropdown can be read down; the cache still learns every match, which is the cheap part.</summary>
    public const int MaximumSuggestions = 20;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SearchTickersResult>> Handle(SearchTickersQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var trimmed = (query.Query ?? string.Empty).Trim();

        // No provider call at all for a query too short to mean anything: every keystroke reaches here.
        if (trimmed.Length < MinimumQueryLength)
        {
            return [];
        }

        var matches = await provider.SearchSymbolsAsync(trimmed, ct);

        if (matches.Count == 0)
        {
            return [];
        }

        // Every match, not just the suggested ones: writing them is nearly free and warms the cache for
        // whichever symbol the user is about to pick.
        await names.WriteAsync(matches, ct);

        return
        [
            .. matches
                .Take(MaximumSuggestions)
                .Select(match => new SearchTickersResult(match.Ticker.Value, match.Name)),
        ];
    }
}
