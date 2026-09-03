using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Events;

/// <summary>
/// The base shape every domain event this aggregate raises shares. Six of
/// the seven envelope fields of specs/shared/domain-model.md §7.1 are fixed
/// here, inside the domain, at the moment the fact becomes true —
/// <see cref="EventId"/> and <see cref="OccurredAt"/> included, per §7.1's
/// own requirement that both are stamped in the domain, not at publish
/// time. The seventh, <c>payload</c>, is each subtype's own fields
/// (design.md §7.1).
/// </summary>
/// <remarks>
/// Implements <see cref="IDomainEventEnvelope"/> (feature
/// outbox_and_idempotency, design.md §4.7) — additive: every member below
/// already existed with these exact names and types, so this is a
/// declaration that the shape already satisfies the interface, not a
/// behaviour change. The outbox writer calls
/// <see cref="DomainEventEnvelope.Validate"/> against every event before it
/// builds a row (R11).
/// </remarks>
public abstract record OrderDomainEvent(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt) : IDomainEvent, IDomainEventEnvelope
{
    /// <summary>The wire <c>eventType</c>, e.g. <c>order.placed.v1</c> — matching <c>&lt;aggregate&gt;.&lt;fact&gt;.v&lt;n&gt;</c> (R11).</summary>
    public abstract string EventType { get; }
}
