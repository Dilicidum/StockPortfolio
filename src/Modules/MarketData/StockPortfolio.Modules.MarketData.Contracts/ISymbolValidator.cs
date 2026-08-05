namespace StockPortfolio.Modules.MarketData.Contracts;

/// <summary>Existence, split off from price because the two degrade in opposite directions.</summary>
public interface ISymbolValidator
{
    /// <summary>Whether the provider recognises this symbol. Returns true when the provider cannot answer —
    /// a purchase must not be rejected because Finnhub is down.</summary>
    Task<bool> IsKnownSymbolAsync(string ticker, CancellationToken ct);
}
