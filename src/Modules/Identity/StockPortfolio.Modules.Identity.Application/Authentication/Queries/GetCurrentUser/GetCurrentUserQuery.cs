using System.Diagnostics.CodeAnalysis;

// CA1716 flags the `Me` namespace segment because `Me` is a Visual Basic keyword.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "The `Me` segment is fixed by the frozen Identity contract and mirrors the /api/auth/me route; the solution is C#-only.",
    Scope = "namespaceanddescendants",
    Target = "~N:StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser")]

namespace StockPortfolio.Modules.Identity.Application.Authentication.Queries.GetCurrentUser;

/// <summary>Read the signed-in user.</summary>
public sealed record GetCurrentUserQuery(Guid UserId);
