namespace StockPortfolio.Shared.Kernel;

/// <summary>
/// Something that happened inside the domain and that other parts of the system may care about.
/// Raised by an <see cref="AggregateRoot{TId}"/> and drained by the layer that persists it.
/// </summary>
public interface IDomainEvent
{
    /// <summary>Gets the instant the event happened, supplied by the aggregate's clock.</summary>
    DateTimeOffset OccurredAt { get; }
}
