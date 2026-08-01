namespace StockPortfolio.Shared.Kernel.Cqrs;

/// <summary>
/// A single rule failure, carried as a case of a handler's result union or returned by a
/// domain factory.
/// </summary>
/// <param name="Field">The name of the offending field, as the client spelled it.</param>
/// <param name="Message">A message safe to show the client.</param>
/// <remarks>
/// This is the <i>invariant</i> and <i>context</i> half of validation. Shape validation —
/// "is this even an email?" — never reaches here: it is a FluentValidation rule on the HTTP
/// request, applied by <c>ValidationFilter&lt;T&gt;</c>, which returns 400 before any handler runs.
/// </remarks>
public sealed record ValidationFailed(string Field, string Message);
