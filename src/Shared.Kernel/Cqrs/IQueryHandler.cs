namespace StockPortfolio.Shared.Kernel.Cqrs;

/// <summary>
/// Handles one query — a request that reads state and changes nothing.
/// </summary>
/// <typeparam name="TQuery">The query. One per read.</typeparam>
/// <typeparam name="TResult">
/// The result union, normally an <c>OneOf</c> whose cases are the projection plus every
/// context failure the handler can decide on.
/// </typeparam>
public interface IQueryHandler<in TQuery, TResult>
{
    /// <summary>Executes the query.</summary>
    /// <param name="query">The query to execute.</param>
    /// <param name="ct">Cancels the operation, normally <c>HttpContext.RequestAborted</c>.</param>
    /// <returns>The result union.</returns>
    Task<TResult> Handle(TQuery query, CancellationToken ct);
}
