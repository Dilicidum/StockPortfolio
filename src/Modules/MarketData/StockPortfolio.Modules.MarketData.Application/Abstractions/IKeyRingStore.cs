namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

public interface IKeyRingStore
{
    IReadOnlyList<string> GetAll();

    void Store(string friendlyName, string xml);
}
