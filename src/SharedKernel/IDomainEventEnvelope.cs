namespace OrderToCash.SharedKernel;

/// <summary>
/// The six envelope fields every domain event must carry so that feature
/// <c>outbox_and_idempotency</c> can turn a raised <see cref="IDomainEvent"/>
/// into a complete wire envelope (specs/shared/domain-model.md §7.1; R11,
/// R12). The seventh field, <c>payload</c>, is not here: <c>SharedKernel</c>
/// carries zero package references and must not know what JSON is (design.md
/// §4.7) — payload completeness is checked at the outbox writer, which owns
/// serialisation.
/// </summary>
public interface IDomainEventEnvelope : IDomainEvent
{
    /// <summary>Minted in the domain when the fact became true, never at publish time.</summary>
    UniqueId EventId { get; }

    /// <summary>The wire <c>eventType</c>, matching <c>&lt;aggregate&gt;.&lt;fact&gt;.v&lt;n&gt;</c> (R11).</summary>
    string EventType { get; }

    UniqueId AggregateId { get; }

    /// <summary>Always the order id (specs/shared/domain-model.md §7.1).</summary>
    UniqueId CorrelationId { get; }

    /// <summary>The id of the command or fact that caused this one — a required parameter of every aggregate method, never defaulted (R12).</summary>
    UniqueId CausationId { get; }

    /// <summary>Stamped by the aggregate, not when it was published or consumed.</summary>
    DateTimeOffset OccurredAt { get; }
}
