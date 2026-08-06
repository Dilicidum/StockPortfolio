namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>Where the protector's own keys are kept. Synchronous, because IXmlRepository is.</summary>
public interface IKeyRingStore
{
    IReadOnlyList<string> GetAll();

    void Store(string friendlyName, string xml);
}
