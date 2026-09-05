using OrderToCash.Contracts.Facts;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Events;

/// <summary>
/// Raised by <see cref="OrderStockReservation.Release"/> when at least one
/// reservation of the order was released (`R34`; F5 — an already-released
/// order emits nothing at all; specs/shared/asyncapi.yaml
/// <c>StockReleasedPayload</c>).
/// </summary>
public sealed record StockReleased(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string CompanyCode,
    string? RetailerCode,
    IReadOnlyList<ReservationRef> Released,
    string Reason)
    : StockDomainEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "stock.released.v1";
}
