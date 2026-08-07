namespace StockPortfolio.Modules.MarketData.Application.Abstractions;

public interface IUserProviderKeyReader
{
    Task<string?> ReadPlaintextAsync(Guid userId, CancellationToken ct);
}
