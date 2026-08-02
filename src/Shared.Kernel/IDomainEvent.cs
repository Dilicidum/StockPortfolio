namespace StockPortfolio.Shared.Kernel;

/// <summary>
/// Something that happened inside the domain and that other parts of the system may care about.
/// </summary>
/// <remarks>
/// Nothing raises one yet. Phase 4 needs a <c>HoldingRemoved</c> event so Alerts can clear a
/// pending cooldown, and this is the shape it will take. There is deliberately no
/// <c>AggregateRoot</c> base class collecting events — entities own their own identity and
/// invariants, and the collect/drain mechanism will be added where it is actually needed rather
/// than imposed on every entity in advance.
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Gets the instant the event happened, supplied by the caller's clock.</summary>
    DateTimeOffset OccurredAt { get; }
}
