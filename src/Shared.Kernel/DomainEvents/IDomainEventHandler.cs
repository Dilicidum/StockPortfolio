namespace StockPortfolio.Shared.Kernel.DomainEvents;

/// <summary>Reacts to one kind of domain event, in whichever module cares.</summary>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    /// <summary>Handles the event. Runs after the originating save has committed.</summary>
    Task Handle(TEvent domainEvent, CancellationToken ct);
}
