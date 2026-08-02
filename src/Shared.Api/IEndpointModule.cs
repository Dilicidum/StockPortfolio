using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Routing;

// CA1716 flags the `Shared` segment because it is a Visual Basic keyword. The name is fixed by the
// project layout (StockPortfolio.Shared.Api is an assembly name, not just a namespace) and
// this is a C#-only solution with no cross-language consumers, so the rule buys nothing here.
// Suppressed for this assembly's namespaces only, never repo-wide.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "The `Shared` segment is fixed by the assembly name; the solution is C#-only.",
    Scope = "namespaceanddescendants",
    Target = "~N:StockPortfolio.Shared.Api")]

namespace StockPortfolio.Shared.Api;

/// <summary>
/// A module's inbound HTTP surface, in one place.
/// </summary>
/// <remarks>
/// Defined and deliberately unused in Phase 1: <c>app.MapIdentityEndpoints()</c> is one line in
/// the host, trim-safe and explicitly ordered, where assembly scanning is neither. The interface
/// exists so the seam is named the day a module wants to be discovered rather than called.
/// <para>
/// This type takes an <see cref="IEndpointRouteBuilder"/>, which is why it lives here and not in
/// Shared.Kernel — the kernel must not pull <c>Microsoft.AspNetCore.App</c> onto every Domain project.
/// </para>
/// </remarks>
public interface IEndpointModule
{
    /// <summary>Registers every route the module owns.</summary>
    /// <param name="app">The route builder to map onto.</param>
    void MapEndpoints(IEndpointRouteBuilder app);
}
