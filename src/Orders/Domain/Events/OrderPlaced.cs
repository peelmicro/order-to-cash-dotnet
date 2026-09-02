using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Domain.Events;

/// <summary>One line of an order as it was at the moment of <see cref="OrderPlaced"/> — a domain-typed snapshot, never a Contracts payload type (design.md §7.2).</summary>
public sealed record OrderPlacedLine(
    string ProductCode,
    string? Description,
    Quantity Quantity,
    Money UnitPrice,
    Money LineDiscount);

/// <summary>
/// Raised by <see cref="Order.Place"/> — T-1 row 1, the fact that starts the
/// saga (specs/shared/domain-model.md §7.2, fact 1;
/// specs/shared/asyncapi.yaml <c>OrderPlacedPayload</c>).
/// </summary>
public sealed record OrderPlaced(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string RetailerCode,
    string CompanyCode,
    GLN BuyerGln,
    GLN SupplierGln,
    string Currency,
    DateTimeOffset OrderDate,
    IReadOnlyList<OrderPlacedLine> Lines,
    Money InitialAmount,
    Money InitialDiscount,
    Money TotalAmount,
    string? Notes)
    : OrderDomainEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "order.placed.v1";
}
