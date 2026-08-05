using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>Where prices come from. One implementation is live, one is generated; the host picks.</summary>
public interface IQuoteProvider
{
    /// <summary>The name the startup log line, the health route and the integration fixture all read.</summary>
    string Name { get; }

    /// <summary>Fetches what it can. A symbol that failed or priced at zero is absent, never zero-valued.</summary>
    Task<IReadOnlyList<Quote>> GetQuotesAsync(IReadOnlySet<Ticker> tickers, CancellationToken ct);

    /// <summary>Whether the provider recognises this symbol; true when it cannot answer.</summary>
    Task<bool> SymbolExistsAsync(Ticker ticker, CancellationToken ct);
}
