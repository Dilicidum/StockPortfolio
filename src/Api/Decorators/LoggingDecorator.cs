using System.Diagnostics;

using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Api.Decorators;

/// <summary>
/// The log messages the two handler decorators emit.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LoggerMessage.Define{T}(LogLevel, EventId, string)"/> rather than
/// <c>logger.LogDebug(...)</c>: the delegates are built once, statically, so a suppressed log level
/// costs a boolean rather than boxing every argument. Written by hand rather than with the
/// <c>[LoggerMessage]</c> source generator because the call sites are generic classes, and the
/// generator's diagnostics around generic containers are a trap not worth stepping into for three
/// messages.
/// </para>
/// <para>
/// The <i>command</i> is never logged, only its type name. Commands here carry passwords and refresh
/// tokens; a structured logger that captured them would put credentials in Application Insights forever.
/// </para>
/// </remarks>
internal static class HandlerLog
{
    private static readonly Action<ILogger, string, Exception?> StartingCallback =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1001, "HandlerStarting"),
            "Handling {Handler}.");

    private static readonly Action<ILogger, string, long, Exception?> CompletedCallback =
        LoggerMessage.Define<string, long>(
            LogLevel.Debug,
            new EventId(1002, "HandlerCompleted"),
            "Handled {Handler} in {ElapsedMilliseconds} ms.");

    private static readonly Action<ILogger, string, long, Exception?> FaultedCallback =
        LoggerMessage.Define<string, long>(
            LogLevel.Error,
            new EventId(1003, "HandlerFaulted"),
            "{Handler} threw after {ElapsedMilliseconds} ms.");

    /// <summary>Logs that a handler is about to run.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="handler">The handler's type name.</param>
    internal static void HandlerStarting(this ILogger logger, string handler) =>
        StartingCallback(logger, handler, null);

    /// <summary>Logs that a handler returned normally.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="handler">The handler's type name.</param>
    /// <param name="elapsedMilliseconds">Wall-clock duration of the call.</param>
    internal static void HandlerCompleted(this ILogger logger, string handler, long elapsedMilliseconds) =>
        CompletedCallback(logger, handler, elapsedMilliseconds, null);

    /// <summary>Logs that a handler threw.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="handler">The handler's type name.</param>
    /// <param name="elapsedMilliseconds">Wall-clock duration before the throw.</param>
    /// <param name="exception">The exception, rethrown by the caller.</param>
    internal static void HandlerFaulted(
        this ILogger logger,
        string handler,
        long elapsedMilliseconds,
        Exception exception) =>
        FaultedCallback(logger, handler, elapsedMilliseconds, exception);
}

/// <summary>
/// Logs the start, duration and failure of every <see cref="ICommandHandler{TCommand, TResult}"/>.
/// </summary>
/// <typeparam name="TCommand">The command type.</typeparam>
/// <typeparam name="TResult">The handler's result union.</typeparam>
/// <param name="inner">The decorated handler.</param>
/// <param name="logger">The logger for this closed generic.</param>
/// <remarks>
/// This is the "cross-cutting concerns are DI decorators" convention in CLAUDE.md, and the reason
/// there is no mediator: a pipeline behaviour needs a dispatcher to hang off, a decorator does not.
/// Validation is deliberately <i>not</i> here — it is an <c>IEndpointFilter</c>, because a decorator
/// cannot manufacture a failure value of an unconstrained <typeparamref name="TResult"/>.
/// </remarks>
internal sealed class LoggingCommandHandler<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    ILogger<LoggingCommandHandler<TCommand, TResult>> logger)
    : ICommandHandler<TCommand, TResult>
{
    /// <inheritdoc/>
    public async Task<TResult> Handle(TCommand command, CancellationToken ct)
    {
        var name = typeof(TCommand).Name;
        logger.HandlerStarting(name);

        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.Handle(command, ct).ConfigureAwait(false);
            logger.HandlerCompleted(name, ElapsedMilliseconds(started));
            return result;
        }
        catch (Exception exception)
        {
            logger.HandlerFaulted(name, ElapsedMilliseconds(started), exception);
            throw;
        }
    }

    private static long ElapsedMilliseconds(long startedAt) =>
        (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
}

/// <summary>
/// Logs the start, duration and failure of every <see cref="IQueryHandler{TQuery, TResult}"/>.
/// </summary>
/// <typeparam name="TQuery">The query type.</typeparam>
/// <typeparam name="TResult">The handler's result union.</typeparam>
/// <param name="inner">The decorated handler.</param>
/// <param name="logger">The logger for this closed generic.</param>
/// <remarks>
/// A near-duplicate of <see cref="LoggingCommandHandler{TCommand, TResult}"/> and deliberately not
/// unified with it. <c>ICommandHandler</c> and <c>IQueryHandler</c> are separate interfaces with no
/// common base by design — a shared base would let a query be dispatched as a command — so the only
/// way to share the body would be to invent that base for the decorator's convenience.
/// </remarks>
internal sealed class LoggingQueryHandler<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    ILogger<LoggingQueryHandler<TQuery, TResult>> logger)
    : IQueryHandler<TQuery, TResult>
{
    /// <inheritdoc/>
    public async Task<TResult> Handle(TQuery query, CancellationToken ct)
    {
        var name = typeof(TQuery).Name;
        logger.HandlerStarting(name);

        var started = Stopwatch.GetTimestamp();

        try
        {
            var result = await inner.Handle(query, ct).ConfigureAwait(false);
            logger.HandlerCompleted(name, ElapsedMilliseconds(started));
            return result;
        }
        catch (Exception exception)
        {
            logger.HandlerFaulted(name, ElapsedMilliseconds(started), exception);
            throw;
        }
    }

    private static long ElapsedMilliseconds(long startedAt) =>
        (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
}
