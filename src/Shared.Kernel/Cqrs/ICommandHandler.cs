namespace StockPortfolio.Shared.Kernel.Cqrs;

/// <summary>Handles one command — a request that changes state.</summary>
public interface ICommandHandler<in TCommand, TResult>
{
    /// <summary>Executes the command.</summary>
    Task<TResult> Handle(TCommand command, CancellationToken ct);
}
