namespace StockPortfolio.Shared.Kernel.Cqrs;

public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> Handle(TQuery query, CancellationToken ct);
}
