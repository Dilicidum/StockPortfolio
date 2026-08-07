namespace StockPortfolio.Modules.MarketData.Contracts;

public sealed record PriceWindow(
    string Ticker,
    decimal Current,
    decimal Oldest,
    decimal Low,
    decimal High,
    DateTimeOffset OldestAt,
    DateTimeOffset NewestAt,
    int SampleCount,
    TimeSpan LargestGap);

public interface IPriceWindowReader
{
    Task<PriceWindow?> GetWindowAsync(string ticker, TimeSpan window, CancellationToken ct);
}
