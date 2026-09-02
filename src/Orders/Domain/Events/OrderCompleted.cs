using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Events;

/// <summary>
/// Raised by <c>Order.Complete</c> — T-1 row 8, the saga closing
/// successfully (specs/shared/domain-model.md §7.2, fact 12;
/// specs/shared/asyncapi.yaml <c>OrderCompletedPayload</c>).
/// </summary>
public sealed record OrderCompleted(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    Money TotalAmount,
    DateTimeOffset CompletedAt)
    : OrderDomainEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "order.completed.v1";
}
