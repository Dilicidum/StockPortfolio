namespace StockPortfolio.Modules.MarketData.Contracts;

public sealed record FeedHealth(
    DateTimeOffset? LastCycleAt,
    int TickersTargeted,
    int TickersStored,
    string ProviderName,
    bool ProviderKeyRejected);

public interface IFeedHealth
{
    Task<FeedHealth> GetFeedHealthAsync(CancellationToken ct);
}
