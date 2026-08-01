using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

// CA1716 flags the `Shared` segment because it is a Visual Basic keyword. The name is fixed by the
// project layout (StockPortfolio.Shared.Kernel is an assembly name, not just a namespace) and this
// is a C#-only solution with no cross-language consumers, so the rule buys nothing here. Suppressed
// for this assembly's namespaces only, never repo-wide.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "The `Shared` segment is fixed by the assembly name; the solution is C#-only.",
    Scope = "namespaceanddescendants",
    Target = "~N:StockPortfolio.Shared.Kernel")]

namespace StockPortfolio.Shared.Kernel;

/// <summary>
/// Base class for the entity that owns a consistency boundary.
/// </summary>
/// <typeparam name="TId">
/// The strongly-typed id of the aggregate. Constrained to <see langword="struct"/> so an id is
/// never null and never shares a reference with another aggregate.
/// </typeparam>
/// <remarks>
/// <para>
/// <c>Id</c> is declared here and <b>only</b> here. A derived entity that re-declares it produces
/// CS0108, which is a build error in this repository. EF Core maps the inherited property normally.
/// </para>
/// <para>
/// <see cref="NotMappedAttribute"/> lives in <c>System.ComponentModel.Annotations</c>, part of the
/// shared framework, so this type needs no EF Core reference. Shared.Kernel stays framework-free.
/// </para>
/// </remarks>
public abstract class AggregateRoot<TId>
    where TId : struct
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Gets the identity of the aggregate. Set once, by the static factory of the entity.</summary>
    public TId Id { get; protected set; } = default!;

    /// <summary>Gets the events raised since the aggregate was loaded or last cleared.</summary>
    [NotMapped]
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>Records a domain event. Callable only from inside the aggregate.</summary>
    /// <param name="e">The event that happened.</param>
    protected void Raise(IDomainEvent e) => _domainEvents.Add(e);

    /// <summary>Drops every recorded event. Called after the events have been dispatched.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();
}
