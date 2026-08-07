using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Prices;

public sealed class SymbolValidator(IQuoteProvider provider) : ISymbolValidator
{
    public Task<bool> IsKnownSymbolAsync(string ticker, CancellationToken ct) =>
        Ticker.Create(ticker).Match(
            parsed => provider.SymbolExistsAsync(parsed, ct),
            badTicker => Task.FromResult(false));
}
