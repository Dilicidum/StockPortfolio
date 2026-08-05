using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>The fallback store. Every method is best-effort: a store failure degrades, it never throws out.</summary>
public interface ILastKnownPriceStore
{
    /// <summary>Reads the last recorded price for each ticker. A ticker with none is absent from the result.</summary>
    Task<IReadOnlyDictionary<Ticker, LastPrice>> ReadAsync(
        IReadOnlyCollection<Ticker> tickers,
        CancellationToken ct);

    /// <summary>Records every quote. The caller is QuoteReader, so both provider paths record identically.</summary>
    Task WriteAsync(IReadOnlyCollection<Quote> quotes, CancellationToken ct);
}
