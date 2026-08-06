namespace StockPortfolio.Modules.MarketData.Contracts;

/// <summary>One ticker's recent series, reduced to the five numbers an alert rule needs plus its shape.</summary>
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

/// <summary>The one history read Alerts makes of MarketData. A ticker with no series is absent.</summary>
public interface IPriceWindowReader
{
    /// <summary>Reads the samples inside the window and reduces them; returns null when there are none.</summary>
    Task<PriceWindow?> GetWindowAsync(string ticker, TimeSpan window, CancellationToken ct);
}
