namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

public interface IPollTargetSource
{
    Task<IReadOnlyList<string>> GetPollTargetsAsync(CancellationToken ct);
}
