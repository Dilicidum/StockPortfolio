namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

/// <summary>The tickers worth sampling this cycle; an empty list is the ordinary case and means no work.</summary>
public interface IPollTargetSource
{
    Task<IReadOnlyList<string>> GetPollTargetsAsync(CancellationToken ct);
}
