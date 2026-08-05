namespace StockPortfolio.Modules.Portfolio.Contracts;

/// <summary>Whether a user has a position in a ticker, so Alerts can reject a subscription on something they do not own.</summary>
public interface IUserHoldsTicker
{
    /// <summary>Returns whether this user holds this ticker, including a hidden position.</summary>
    Task<bool> HoldsAsync(Guid userId, string ticker, CancellationToken ct);
}
