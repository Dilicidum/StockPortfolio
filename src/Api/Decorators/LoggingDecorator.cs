using System.Diagnostics;

using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Api.Decorators;

/// <summary>The log messages the two handler decorators emit.</summary>
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
    internal static void HandlerStarting(this ILogger logger, string handler) =>
        StartingCallback(logger, handler, null);

    /// <summary>Logs that a handler returned normally.</summary>
    internal static void HandlerCompleted(this ILogger logger, string handler, long elapsedMilliseconds) =>
        CompletedCallback(logger, handler, elapsedMilliseconds, null);

    /// <summary>Logs that a handler threw.</summary>
    internal static void HandlerFaulted(
        this ILogger logger,
        string handler,
        long elapsedMilliseconds,
        Exception exception) =>
        FaultedCallback(logger, handler, elapsedMilliseconds, exception);
}

/// <summary>Logs the start, duration and failure of every .</summary>
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

/// <summary>Logs the start, duration and failure of every .</summary>
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
