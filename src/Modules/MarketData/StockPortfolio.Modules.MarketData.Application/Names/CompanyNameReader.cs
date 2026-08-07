using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Names;

public sealed class CompanyNameReader(ICompanyNameStore store) : ICompanyNameReader
{
    public async Task<IReadOnlyDictionary<string, string>> GetNamesAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tickers);

        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var requested = new HashSet<Ticker>();

        foreach (var candidate in tickers)
        {
            if (Ticker.TryParse(candidate) is { } ticker)
            {
                requested.Add(ticker);
            }
        }

        if (requested.Count == 0)
        {
            return names;
        }

        foreach (var (ticker, name) in await store.ReadAsync([.. requested], ct))
        {
            names[ticker.Value] = name;
        }

        return names;
    }
}
