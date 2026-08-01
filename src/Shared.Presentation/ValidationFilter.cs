using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace StockPortfolio.Shared.Presentation;

/// <summary>
/// Runs FluentValidation over the request DTO of an endpoint and short-circuits with a
/// <c>400 ValidationProblemDetails</c> when it fails.
/// </summary>
/// <typeparam name="TRequest">The request record the endpoint binds.</typeparam>
/// <param name="validator">The single validator for <typeparamref name="TRequest"/>.</param>
/// <remarks>
/// <para>
/// This is a filter rather than a DI decorator on purpose. A decorator would have to
/// <i>return</i> an unconstrained <c>TResult</c> on failure and cannot manufacture one — the
/// <c>OneOf</c> conversion is a user-defined operator on a concrete type, unreachable through a
/// type parameter. A filter sits in the HTTP pipeline and can return a response directly.
/// </para>
/// <para>
/// <see cref="IValidator{T}"/> is injected, never <c>IEnumerable&lt;IValidator&lt;T&gt;&gt;</c>:
/// with the single-instance form a missing registration throws loudly at request time, while the
/// collection form silently validates nothing.
/// </para>
/// </remarks>
public sealed class ValidationFilter<TRequest>(IValidator<TRequest> validator) : IEndpointFilter
    where TRequest : class
{
    /// <summary>Validates the request argument, then either continues or returns 400.</summary>
    /// <param name="context">The endpoint invocation context.</param>
    /// <param name="next">The rest of the pipeline.</param>
    /// <returns>The downstream result, or a validation problem.</returns>
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (context.Arguments.OfType<TRequest>().FirstOrDefault() is not { } request)
        {
            return await next(context).ConfigureAwait(false);
        }

        var result = await validator
            .ValidateAsync(request, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        return result.IsValid
            ? await next(context).ConfigureAwait(false)
            : TypedResults.ValidationProblem(result.ToDictionary());
    }
}
