using System.Diagnostics.CodeAnalysis;

// CA1716 flags the `Me` namespace segment because `Me` is a Visual Basic keyword. The segment is
// fixed by the frozen Identity contract (docs/plan/identity-contracts.md) and mirrors the
// /api/auth/me route; the solution is C#-only with no cross-language consumers. Suppressed for this
// one namespace, never repo-wide.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "The `Me` segment is fixed by the frozen Identity contract and mirrors the /api/auth/me route; the solution is C#-only.",
    Scope = "namespaceanddescendants",
    Target = "~N:StockPortfolio.Modules.Identity.Application.Me")]

namespace StockPortfolio.Modules.Identity.Application.Me;

/// <summary>
/// Read the signed-in user.
/// </summary>
/// <param name="UserId">
/// The id from the access token's <c>sub</c> claim. A raw <see cref="Guid"/>, because the value
/// arrives from the HTTP layer as a string and has not yet earned the strongly-typed id.
/// </param>
public sealed record GetCurrentUser(Guid UserId);
