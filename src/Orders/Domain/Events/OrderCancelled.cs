using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Events;

/// <summary>
/// Raised by <c>Order.Cancel</c> — T-1 rows 9–12, the saga closing by
/// cancellation (specs/shared/domain-model.md §7.2, fact 13;
/// specs/shared/asyncapi.yaml <c>OrderCancelledPayload</c>).
/// <see cref="CompensationSteps"/> is empty for <c>stock_rejected</c> —
/// nothing was ever acquired (R26).
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
    IReadOnlyList<OrderCompensationStep> CompensationSteps)
    : OrderDomainEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "order.cancelled.v1";
}
