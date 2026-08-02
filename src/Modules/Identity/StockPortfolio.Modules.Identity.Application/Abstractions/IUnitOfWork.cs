namespace StockPortfolio.Modules.Identity.Application.Abstractions;

/// <summary>Commits everything the handler changed, in one transaction.</summary>
public interface IUnitOfWork
{
    /// <summary>Writes every tracked change.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
