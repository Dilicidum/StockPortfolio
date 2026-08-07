using Microsoft.AspNetCore.Http;
using StockPortfolio.Shared.Kernel;

namespace StockPortfolio.Shared.Api;

public static class ProblemDetailsExtensions
{
    // IResult, never the concrete TypedResults type: a .Match arm calling one of these must contribute a type every other arm agrees with.

    public static IResult ToValidationProblem(this InvalidInput failure) =>
        TypedResults.ValidationProblem(
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [failure.Field] = [failure.Message],
            });

    public static IResult ConflictProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status409Conflict, title: "Conflict");

    public static IResult UnauthorizedProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized");

    public static IResult NotFoundProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status404NotFound, title: "Not Found");

    public static IResult ServiceUnavailableProblem(string detail) =>
        TypedResults.Problem(detail, statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable");
}
