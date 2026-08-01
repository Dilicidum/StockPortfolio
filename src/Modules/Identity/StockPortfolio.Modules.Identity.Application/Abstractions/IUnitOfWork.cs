namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>
/// Commits everything the handler changed, in one transaction.
/// </summary>
/// <remarks>
/// Separate from the repositories so a handler that touches two aggregates — refresh rotates one
/// session and opens another — commits both or neither.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Writes every tracked change.</summary>
    /// <param name="ct">Cancels the operation.</param>
    /// <returns>A task that completes when the changes are committed.</returns>
    Task SaveChangesAsync(CancellationToken ct);
}
