using System.Diagnostics.CodeAnalysis;

// CA1716 flags the `Shared` segment because it is a Visual Basic keyword.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "The `Shared` segment is fixed by the assembly name; the solution is C#-only.",
    Scope = "namespaceanddescendants",
    Target = "~N:StockPortfolio.Shared.Kernel")]

// CA1711 reserves the `EventHandler` suffix for delegates matching the CLR event pattern.
[assembly: SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "A domain-event handler is not a CLR event delegate; the DDD name is the clearer one.",
    Scope = "type",
    Target = "~T:StockPortfolio.Shared.Kernel.DomainEvents.IDomainEventHandler`1")]
