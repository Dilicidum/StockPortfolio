using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>The company-name cache. Every method is best-effort: a store failure degrades, it never throws out.</summary>
public interface ICompanyNameStore
{
    /// <summary>Reads the cached name for each ticker. A ticker with none is absent from the result.</summary>
    Task<IReadOnlyDictionary<Ticker, string>> ReadAsync(
        IReadOnlyCollection<Ticker> tickers,
        CancellationToken ct);

    /// <summary>Records every match a search returned, not just the one that was picked, each with its own expiry.</summary>
    Task WriteAsync(IReadOnlyCollection<SymbolMatch> matches, CancellationToken ct);
}
