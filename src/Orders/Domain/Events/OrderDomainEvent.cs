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
public abstract record OrderDomainEvent(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt) : IDomainEvent
{
    /// <summary>The wire <c>eventType</c>, e.g. <c>order.placed.v1</c> — matching <c>&lt;aggregate&gt;.&lt;fact&gt;.v&lt;n&gt;</c> (R11).</summary>
    public abstract string EventType { get; }
}
