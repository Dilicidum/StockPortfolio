using System.Diagnostics.CodeAnalysis;

// `Shared` is a Visual Basic keyword; the segment is fixed by the assembly name and this is a C#-only solution.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "The `Shared` segment is fixed by the assembly name; the solution is C#-only.",
    Scope = "namespaceanddescendants",
    Target = "~N:StockPortfolio.Shared.Api")]
