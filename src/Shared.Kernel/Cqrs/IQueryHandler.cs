namespace StockPortfolio.Shared.Kernel.Cqrs;

/// <summary>Handles one query — a request that reads state and changes nothing.</summary>
public interface IQueryHandler<in TQuery, TResult>
{
    /// <summary>Executes the query.</summary>
    Task<TResult> Handle(TQuery query, CancellationToken ct);
}
