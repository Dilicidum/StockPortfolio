using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Persistence;

internal sealed class KeyRingStore(IServiceScopeFactory scopeFactory) : IKeyRingStore
{
    public IReadOnlyList<string> GetAll()
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

        return [.. context.KeyRingEntries.Select(entry => entry.Xml)];
    }

    public void Store(string friendlyName, string xml)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

        context.KeyRingEntries.Add(KeyRingEntry.Create(friendlyName, xml));
        context.SaveChanges();
    }
}
