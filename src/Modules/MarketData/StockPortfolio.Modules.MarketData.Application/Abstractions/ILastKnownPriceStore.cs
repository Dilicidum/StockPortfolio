using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

public interface ILastKnownPriceStore
{
    Task<IReadOnlyDictionary<Ticker, LastPrice>> ReadAsync(
        IReadOnlyCollection<Ticker> tickers,
        CancellationToken ct);

    Task WriteAsync(IReadOnlyCollection<Quote> quotes, CancellationToken ct);
}
