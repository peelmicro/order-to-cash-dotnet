using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Events;

/// <summary>
/// Raised by <c>Order.RecordSagaFailure</c> — the 14th fact, feature
/// <c>observability_reliability</c>'s <c>OR3</c>/<c>R29</c> dead-letter
/// clause (specs/shared/asyncapi.yaml <c>OrderSagaFailedPayload</c>). A
/// saga command was retried to exhaustion (SO4) and parked (SO5) without
/// ever completing; the order stays in its LAST legal status — this is not
/// a T-1 transition, so <see cref="Order.RecordSagaFailure"/> bypasses
/// <c>TransitionTo</c> entirely and mutates no other field. Purely
/// diagnostic (asyncapi.yaml's own word): consumed by the projector only,
/// never by <c>orders.saga</c> (which emits it) or <c>notifications</c>.
/// </summary>
public sealed record OrderSagaFailed(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string Command,
    int Attempts,
    string LastError,
    DateTimeOffset FailedAt)
    : FactEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "order.saga_failed.v1";
}
