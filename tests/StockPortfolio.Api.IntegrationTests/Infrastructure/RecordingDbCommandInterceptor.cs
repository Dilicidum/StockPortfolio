using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

/// <summary>One SQL statement as it was handed to Npgsql: the text the server will parse, and the values that.</summary>
public sealed record ExecutedCommand(string CommandText, IReadOnlyList<CommandParameter> Parameters)
{
    /// <summary>Gets just the parameter values, for the common "did this value travel as data?" check.</summary>
    public IEnumerable<string> ParameterValues => Parameters.Select(parameter => parameter.Value);
}

/// <summary>One parameter as Npgsql received it.</summary>
public sealed record CommandParameter(string Name, string Value);

/// <summary>Records every SQL statement EF Core executes, so a test can assert on the text/parameter split.</summary>
public sealed class RecordingDbCommandInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentQueue<ExecutedCommand> _commands = new();

    /// <summary>Gets a snapshot of every statement recorded so far, oldest first.</summary>
    public IReadOnlyList<ExecutedCommand> Commands => [.. _commands];

    /// <inheritdoc/>
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Record(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    /// <inheritdoc/>
    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Record(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    /// <inheritdoc/>
    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void Record(DbCommand command)
    {
        var parameters = new List<CommandParameter>(command.Parameters.Count);

        foreach (DbParameter parameter in command.Parameters)
        {
            var value = parameter.Value switch
            {
                null or DBNull => string.Empty,
                byte[] bytes => Convert.ToHexString(bytes),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                var other => other.ToString() ?? string.Empty,
            };

            parameters.Add(new CommandParameter(parameter.ParameterName, value));
        }

        _commands.Enqueue(new ExecutedCommand(command.CommandText, parameters));
    }
}
