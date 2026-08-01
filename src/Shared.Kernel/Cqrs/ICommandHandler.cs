namespace StockPortfolio.Shared.Kernel.Cqrs;

/// <summary>
/// Handles one command — a request that changes state.
/// </summary>
/// <typeparam name="TCommand">The command. One per user action.</typeparam>
/// <typeparam name="TResult">
/// The result union, normally an <c>OneOf</c> whose cases are the success value plus every
/// context failure the handler can decide on.
/// </typeparam>
/// <remarks>
/// There is no dispatcher: the handler is injected straight into the endpoint that calls it.
/// One caller per handler means a mediator has nothing to decouple. Cross-cutting concerns
/// (logging) are DI decorators; shape validation is an endpoint filter, not a decorator.
/// </remarks>
public interface ICommandHandler<in TCommand, TResult>
{
    /// <summary>Executes the command.</summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="ct">Cancels the operation, normally <c>HttpContext.RequestAborted</c>.</param>
    /// <returns>The result union.</returns>
    Task<TResult> Handle(TCommand command, CancellationToken ct);
}
