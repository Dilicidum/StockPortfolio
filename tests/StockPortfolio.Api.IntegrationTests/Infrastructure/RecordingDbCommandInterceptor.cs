using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace StockPortfolio.Api.IntegrationTests.Infrastructure;

public sealed record ExecutedCommand(string CommandText, IReadOnlyList<CommandParameter> Parameters)
{
    public IEnumerable<string> ParameterValues => Parameters.Select(parameter => parameter.Value);
}

public sealed record CommandParameter(string Name, string Value);

public sealed class RecordingDbCommandInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentQueue<ExecutedCommand> _commands = new();

    public IReadOnlyList<ExecutedCommand> Commands => [.. _commands];

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Record(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Record(command);
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Record(command);
        return base.ScalarExecuting(command, eventData, result);
    }

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
