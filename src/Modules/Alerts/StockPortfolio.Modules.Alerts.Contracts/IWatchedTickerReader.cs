namespace StockPortfolio.Modules.Alerts.Contracts;

/// <summary>The tickers somebody has an enabled threshold on. An empty list is the ordinary case.</summary>
public interface IWatchedTickerReader
{
    /// <summary>Reads every distinct ticker with at least one enabled setting, canonical upper case.</summary>
    Task<IReadOnlyList<string>> GetWatchedTickersAsync(CancellationToken ct);
}
