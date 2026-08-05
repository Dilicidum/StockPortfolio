using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Shared.Api;

/// <summary>Turns a handler's failure case into an RFC 9457 problem response, so every module fails the same shape.</summary>
public static class ProblemDetailsExtensions
{
    /// <summary>Maps a rule failure decided by a handler or an entity to 400.</summary>
    public static ValidationProblem ToValidationProblem(this InvalidInput failure) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [failure.Field] = [failure.Message],
            });

    /// <summary>Builds a 409 problem response.</summary>
    public static ProblemHttpResult ConflictProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict");

    /// <summary>Builds a 401 problem response.</summary>
    public static ProblemHttpResult UnauthorizedProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");

    /// <summary>Builds a 404 problem response.</summary>
    public static ProblemHttpResult NotFoundProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status404NotFound, title: "Not Found");
}
