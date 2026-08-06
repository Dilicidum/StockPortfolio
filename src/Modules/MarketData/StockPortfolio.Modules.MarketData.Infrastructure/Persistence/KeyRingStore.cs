using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Domain;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Persistence;

/// <summary>The Data Protection key ring, kept in Postgres so a container replacement does not lose it.</summary>
internal sealed class KeyRingStore(IServiceScopeFactory scopeFactory) : IKeyRingStore
{
    /// <inheritdoc/>
    public IReadOnlyList<string> GetAll()
    {
        // A singleton over a scoped DbContext, so a scope is opened per call - the rule QuotePoller follows.
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

        return [.. context.KeyRingEntries.Select(entry => entry.Xml)];
    }

    /// <inheritdoc/>
    public void Store(string friendlyName, string xml)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();

        context.KeyRingEntries.Add(KeyRingEntry.Create(friendlyName, xml));
        context.SaveChanges();
    }
}
