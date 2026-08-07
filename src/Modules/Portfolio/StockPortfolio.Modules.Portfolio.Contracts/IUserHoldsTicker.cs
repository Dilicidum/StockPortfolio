namespace StockPortfolio.Modules.Portfolio.Contracts;

public interface IUserHoldsTicker
{
    Task<bool> HoldsAsync(Guid userId, string ticker, CancellationToken ct);
}
