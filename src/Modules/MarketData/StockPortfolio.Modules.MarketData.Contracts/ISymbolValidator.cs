namespace StockPortfolio.Modules.MarketData.Contracts;

public interface ISymbolValidator
{
    Task<bool> IsKnownSymbolAsync(string ticker, CancellationToken ct);
}
