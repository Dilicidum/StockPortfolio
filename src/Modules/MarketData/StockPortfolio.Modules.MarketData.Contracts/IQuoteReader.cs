namespace StockPortfolio.Modules.MarketData.Contracts;

/// <summary>One price for one symbol; IsLastKnown marks a value read from the fallback store.</summary>
public sealed record QuotedPrice(string Ticker, decimal Price, DateTimeOffset ObservedAt, bool IsLastKnown);

/// <summary>The one price read another module makes of MarketData.</summary>
public interface IQuoteReader
{
    /// <summary>Asks the provider first, falls back to the last recorded price. A symbol with no price at
    /// all is absent from the result rather than present with zero. Keys are canonical upper case, Ordinal.
    /// userId resolves the caller's own provider key, if they have one, before the fan-out.</summary>
    Task<IReadOnlyDictionary<string, QuotedPrice>> GetCurrentPricesAsync(
        Guid userId,
        IReadOnlyCollection<string> tickers,
        CancellationToken ct);
}
