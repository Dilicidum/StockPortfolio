namespace StockPortfolio.Modules.MarketData.Contracts;

public sealed record QuotedPrice(string Ticker, decimal Price, DateTimeOffset ObservedAt, bool IsLastKnown);

public interface IQuoteReader
{
    Task<IReadOnlyDictionary<string, QuotedPrice>> GetCurrentPricesAsync(
        Guid userId,
        IReadOnlyCollection<string> tickers,
        CancellationToken ct);
}
