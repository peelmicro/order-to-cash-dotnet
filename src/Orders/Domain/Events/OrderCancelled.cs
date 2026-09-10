using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Events;

/// <summary>
/// Raised by <c>Order.Cancel</c> — T-1 rows 9–12, the saga closing by
/// cancellation (specs/shared/domain-model.md §7.2, fact 13;
/// specs/shared/asyncapi.yaml <c>OrderCancelledPayload</c>).
/// <see cref="CompensationSteps"/> is empty for <c>stock_rejected</c> —
/// nothing was ever acquired (R26). <see cref="Note"/> (SA-2) is the
/// operator's free-text cancellation note, carried only on the
/// <c>operator_cancelled</c> path that has one to carry — every fact-driven
/// caller (<c>SagaFactHandler</c>) leaves it at its default
/// <see langword="null"/>, matching the wire's own optionality.
/// </summary>
public sealed record OrderCancelled(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string RetailerCode,
    string CompanyCode,
    CancellationReason CancellationReason,
    DateTimeOffset CancelledAt,
    IReadOnlyList<OrderCompensationStep> CompensationSteps,
    string? Note = null)
    : FactEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "order.cancelled.v1";
}
