namespace StockPortfolio.Shared.Kernel.DomainEvents;

/// <summary>Delivers raised events to their handlers.</summary>
public interface IDomainEventPublisher
{
    /// <summary>Delivers every event to every handler registered for its concrete type.</summary>
    Task PublishAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken ct);
}
