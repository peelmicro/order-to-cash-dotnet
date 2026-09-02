using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Events;

/// <summary>
/// Raised by <c>Order.Confirm</c> — T-1 row 4, the ORDRSP moment
/// (specs/shared/domain-model.md §7.2, fact 8;
/// specs/shared/asyncapi.yaml <c>OrderConfirmedPayload</c>).
/// </summary>
public sealed record OrderConfirmed(
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
    DateTimeOffset ConfirmedAt)
    : OrderDomainEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "order.confirmed.v1";
}
