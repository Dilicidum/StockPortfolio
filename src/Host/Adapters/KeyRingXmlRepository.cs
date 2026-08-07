using System.Xml.Linq;

using Microsoft.AspNetCore.DataProtection.Repositories;

using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Host.Adapters;

internal sealed class KeyRingXmlRepository(IKeyRingStore store) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements() =>
        [.. store.GetAll().Select(XElement.Parse)];

    public void StoreElement(XElement element, string friendlyName) =>
        store.Store(friendlyName, element.ToString(SaveOptions.DisableFormatting));
}
