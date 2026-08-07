using StockPortfolio.Modules.MarketData.Application.Abstractions;
using StockPortfolio.Modules.MarketData.Contracts;
using StockPortfolio.Modules.MarketData.Infrastructure.Quotes;

namespace StockPortfolio.Modules.MarketData.Infrastructure.Health;

internal sealed class FeedHealthReader(
    IPollHeartbeatStore heartbeats,
    IQuoteProvider provider,
    ProviderKeyRejection rejection) : IFeedHealth
{
    public async Task<FeedHealth> GetFeedHealthAsync(CancellationToken ct)
    {
        var heartbeat = await heartbeats.ReadAsync(ct);

        return new FeedHealth(
            heartbeat?.At,
            heartbeat?.TickersTargeted ?? 0,
            heartbeat?.TickersStored ?? 0,
            provider.Name,
            rejection.IsRejected);
    }
}
