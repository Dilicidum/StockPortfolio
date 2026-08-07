using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

public interface ICompanyNameStore
{
    Task<IReadOnlyDictionary<Ticker, string>> ReadAsync(
        IReadOnlyCollection<Ticker> tickers,
        CancellationToken ct);

    Task WriteAsync(IReadOnlyCollection<SymbolMatch> matches, CancellationToken ct);
}
