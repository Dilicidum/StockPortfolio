namespace StockPortfolio.Modules.Alerts.Contracts;

public interface IWatchedTickerReader
{
    Task<IReadOnlyList<string>> GetWatchedTickersAsync(CancellationToken ct);
}
