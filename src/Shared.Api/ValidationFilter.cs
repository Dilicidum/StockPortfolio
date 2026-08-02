using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace StockPortfolio.Shared.Api;

/// <summary>Runs FluentValidation over the request DTO of an endpoint and short-circuits with a 400.</summary>
public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    /// <summary>Validates the request argument, then either continues or returns 400.</summary>
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (context.Arguments.OfType<TRequest>().FirstOrDefault() is not { } request)
        {
            return await next(context);
        }

        var result = await validator
            .ValidateAsync(request, context.HttpContext.RequestAborted);

        return result.IsValid
            ? await next(context)
            : TypedResults.ValidationProblem(result.ToDictionary());
    }
}
