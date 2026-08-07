namespace StockPortfolio.Modules.MarketData.Contracts;

public interface ICompanyNameReader
{
    Task<IReadOnlyDictionary<string, string>> GetNamesAsync(
        IReadOnlyCollection<string> tickers,
        CancellationToken ct);
}
