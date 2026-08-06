using System.Xml.Linq;

using Microsoft.AspNetCore.DataProtection.Repositories;

using StockPortfolio.Modules.MarketData.Application.Abstractions;

namespace StockPortfolio.Api.Adapters;

/// <summary>Adapts the framework's XML key ring onto MarketData's own store, so Postgres holds it.</summary>
internal sealed class KeyRingXmlRepository(IKeyRingStore store) : IXmlRepository
{
    /// <inheritdoc/>
    public IReadOnlyCollection<XElement> GetAllElements() =>
        [.. store.GetAll().Select(XElement.Parse)];

    /// <inheritdoc/>
    public void StoreElement(XElement element, string friendlyName) =>
        store.Store(friendlyName, element.ToString(SaveOptions.DisableFormatting));
}
