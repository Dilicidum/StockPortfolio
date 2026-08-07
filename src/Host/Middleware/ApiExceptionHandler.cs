using System.Data.Common;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace StockPortfolio.Host.Middleware;

internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    /// <summary>Npgsql reports an exhausted pool as a bare DbException with no inner exception, so IsTransient is false and only the message says what happened.</summary>
    private const string PoolExhaustedMessage = "pool has been exhausted";

    private const string RetryAfterSeconds = "5";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // No logging here: ExceptionHandlerMiddleware logs the exception before it calls this.
        var statusCode = IsDatabaseUnavailable(exception)
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status500InternalServerError;

        httpContext.Response.StatusCode = statusCode;

        if (statusCode == StatusCodes.Status503ServiceUnavailable)
        {
            httpContext.Response.Headers.RetryAfter = RetryAfterSeconds;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = statusCode,
                // No Title and no Type: ProblemDetailsDefaults fills both, so this handler and the framework use one namespace.
                // Deliberately no Detail: never surface exception text to a caller.
            },
        });
    }

    /// <summary>True when the database could not be reached, false for a write the database rejected — a unique-index violation must stay a 500.</summary>
    private static bool IsDatabaseUnavailable(Exception exception)
    {
        // The thrown exception itself first: a failed SELECT throws NpgsqlException with nothing wrapping it, while a failed save is inside DbUpdateException and an exhausted retry inside RetryLimitExceededException.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException database
                && (database.IsTransient
                    || database.Message.Contains(PoolExhaustedMessage, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
