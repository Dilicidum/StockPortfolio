namespace StockPortfolio.Shared.Kernel.Cqrs;

public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> Handle(TCommand command, CancellationToken ct);
}
