using System.Diagnostics.CodeAnalysis;

// CA1716 flags the `Shared` segment because it is a Visual Basic keyword.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "The `Shared` segment is fixed by the assembly name; the solution is C#-only.",
    Scope = "namespaceanddescendants",
    Target = "~N:StockPortfolio.Shared.Kernel")]
