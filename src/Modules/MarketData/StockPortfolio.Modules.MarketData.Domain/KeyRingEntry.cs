namespace StockPortfolio.Modules.MarketData.Domain;

public sealed class KeyRingEntry
{
    private KeyRingEntry(Guid id, string friendlyName, string xml)
    {
        Id = id;
        FriendlyName = friendlyName;
        Xml = xml;
    }

    public Guid Id { get; private set; }

    public string FriendlyName { get; private set; }

    public string Xml { get; private set; }

    public static KeyRingEntry Create(string friendlyName, string xml) =>
        new(Guid.CreateVersion7(), friendlyName, xml);
}
