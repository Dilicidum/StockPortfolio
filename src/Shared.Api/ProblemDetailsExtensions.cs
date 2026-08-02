using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using StockPortfolio.Shared.Kernel.Cqrs;

namespace StockPortfolio.Shared.Api;

/// <summary>
/// Turns a handler's failure case into an RFC 9457 problem response, so every module answers a
/// failure the same way and no endpoint hand-rolls a status code.
/// </summary>
/// <remarks>
/// Deliberately thin. These are the four shapes Phase 1 needs plus the
/// <see cref="ValidationFailed"/> mapping; anything richer belongs in the module that needs it.
/// </remarks>
public static class ProblemDetailsExtensions
{
    /// <summary>Maps a rule failure decided by a handler or an entity to <c>400</c>.</summary>
    /// <param name="failure">The failure to report.</param>
    /// <returns>A <c>400</c> carrying <c>ValidationProblemDetails</c> keyed by field.</returns>
    public static ValidationProblem ToValidationProblem(this ValidationFailed failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return TypedResults.ValidationProblem(
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [failure.Field] = [failure.Message],
            });
    }

    /// <summary>Builds a <c>404</c> problem response.</summary>
    /// <param name="detail">What was not found, phrased for the client.</param>
    /// <returns>The problem result.</returns>
    public static ProblemHttpResult NotFoundProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status404NotFound, title: "Not Found");

    /// <summary>Builds a <c>409</c> problem response.</summary>
    /// <param name="detail">What conflicted, phrased for the client.</param>
    /// <returns>The problem result.</returns>
    public static ProblemHttpResult ConflictProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict");

    /// <summary>Builds a <c>401</c> problem response. Says nothing about which credential was wrong.</summary>
    /// <param name="detail">A message that leaks nothing, for example "Invalid credentials.".</param>
    /// <returns>The problem result.</returns>
    public static ProblemHttpResult UnauthorizedProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");

    /// <summary>Builds a <c>403</c> problem response.</summary>
    /// <param name="detail">Why the caller may not do this.</param>
    /// <returns>The problem result.</returns>
    public static ProblemHttpResult ForbiddenProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status403Forbidden, title: "Forbidden");
}
