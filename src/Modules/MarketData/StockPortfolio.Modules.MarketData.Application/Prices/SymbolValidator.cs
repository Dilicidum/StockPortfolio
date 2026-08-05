using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Prices;

/// <summary>Existence, failing open: a Finnhub outage must not reject a valid purchase.</summary>
public sealed class SymbolValidator(IQuoteProvider provider) : ISymbolValidator
{
    public Task<bool> IsKnownSymbolAsync(string ticker, CancellationToken ct) =>
        Ticker.Create(ticker).TryPickT0(out var parsed, out _)
            ? provider.SymbolExistsAsync(parsed, ct)
            : Task.FromResult(false);
}
