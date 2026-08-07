using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace StockPortfolio.Host.Middleware;

internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        // No logging here: ExceptionHandlerMiddleware logs the exception before it calls this.
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
        });
    }
}
