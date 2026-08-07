using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

public interface IQuoteProvider
{
    string Name { get; }

    Task<IReadOnlyList<Quote>> GetQuotesAsync(
        IReadOnlySet<Ticker> tickers, string? apiKeyOverride, CancellationToken ct);

    Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct);

    Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(string query, CancellationToken ct);

    Task<KeyVerdict> VerifyKeyAsync(string apiKey, CancellationToken ct);
}
