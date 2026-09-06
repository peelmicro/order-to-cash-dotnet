using OrderToCash.SharedKernel;

namespace OrderToCash.Billing.Domain.Events;

/// <summary>
/// The base shape every domain event this service raises shares — the
/// neutral <c>FactEvent</c> shape every service now declares in its own
/// <c>Domain.Events</c> namespace (design.md §8.2.2), mirrored exactly. Six
/// of the seven envelope fields of specs/shared/domain-model.md §7.1 are
/// fixed here, inside the domain, at the moment the fact becomes true; the
/// seventh, <c>payload</c>, is each subtype's own fields.
/// </summary>
public abstract record FactEvent(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt) : IDomainEvent, IDomainEventEnvelope
{
    public abstract string EventType { get; }
}
