using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace StockPortfolio.Api.Middleware;

/// <summary>
/// Turns any unhandled exception into an RFC 7807 <c>application/problem+json</c> response.
/// </summary>
/// <remarks>
/// <para>
/// This is the <b>last resort</b>, not the validation path. Shape validation is handled before the
/// route runs by <c>ValidationFilter&lt;T&gt;</c>, which returns a 400 directly; context failures
/// come back from handlers as <c>OneOf</c> cases and are mapped to status codes at the endpoint.
/// Anything that reaches here is a bug, so it is logged at Error and reported as a bare 500 with no
/// exception detail — a stack trace in a response body is an information leak.
/// </para>
/// <para>
/// Domain invariant guards deliberately <see langword="throw"/> (see CLAUDE.md), so a violated
/// invariant also lands here. That is intentional: an invariant reaching the HTTP layer means the
/// layer above failed to check something it should have, which is a defect and not a 400.
/// </para>
/// </remarks>
internal sealed partial class ApiExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    // Source-generated rather than logger.LogError(...): CA1848 is an error in this repo, and the
    // generated delegate avoids boxing the arguments and re-parsing the template on every call.
    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Error,
        Message = "Unhandled exception for {Method} {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string method, string path);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        LogUnhandled(logger, exception, httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://datatracker.ietf.org/doc/html/rfc9110#section-15.6.1",
                // Deliberately no Detail: never surface exception text to a caller.
            },
        }).ConfigureAwait(false);
    }
}
