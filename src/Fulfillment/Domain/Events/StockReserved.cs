using OrderToCash.Contracts.Facts;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Events;

/// <summary>
/// Raised by <see cref="OrderStockReservation.Reserve"/> when every line of
/// the order is satisfiable (`R32`; specs/shared/asyncapi.yaml
/// <c>StockReservedPayload</c>). <see cref="Reservations"/> reuses
/// <see cref="ReservationRef"/> from <c>Contracts.Facts</c> directly — the
/// wire shape is identical, and this is the domain-event-may-reference-Contracts-
/// for-payload-record-types precedent <c>OrderPlaced</c>/<c>OrderPlacedLine</c>
/// does NOT set (those are domain-typed), but which
/// <c>tests/Architecture.Tests/OrdersDomainContractsTests.cs</c>'s equivalent
/// rule for this service (design.md §2, "Layering") allows.
/// </summary>
public sealed record StockReserved(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string CompanyCode,
    string? RetailerCode,
    IReadOnlyList<ReservationRef> Reservations)
    : StockDomainEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "stock.reserved.v1";
}
