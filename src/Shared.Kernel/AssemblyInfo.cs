using System.Diagnostics.CodeAnalysis;

// CA1716 flags the `Shared` segment because it is a Visual Basic keyword. The name is fixed by the
// project layout (StockPortfolio.Shared.Kernel is an assembly name, not just a namespace) and this
// is a C#-only solution with no cross-language consumers, so the rule buys nothing here. Suppressed
// for this assembly's namespaces only, never repo-wide.
//
// This lives in its own file rather than riding along on whichever type happened to be first in the
// assembly — it was previously attached to AggregateRoot.cs and vanished when that type was deleted,
// turning a naming preference into a build failure.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "The `Shared` segment is fixed by the assembly name; the solution is C#-only.",
    Scope = "namespaceanddescendants",
    Target = "~N:StockPortfolio.Shared.Kernel")]
