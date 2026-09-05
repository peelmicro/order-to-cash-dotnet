using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Events;

/// <summary>
/// The base shape every domain event this service raises shares — the
/// <c>OrderDomainEvent</c> shape (<c>src/Orders/Domain/Events/OrderDomainEvent.cs</c>)
/// mirrored exactly, per design.md §3.3. Six of the seven envelope fields of
/// specs/shared/domain-model.md §7.1 are fixed here, inside the domain, at
/// the moment the fact becomes true; the seventh, <c>payload</c>, is each
/// subtype's own fields.
/// </summary>
public abstract record StockDomainEvent(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt) : IDomainEvent, IDomainEventEnvelope
{
    public abstract string EventType { get; }
}
