using OrderToCash.Contracts.Facts;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Events;

/// <summary>
/// Raised by <see cref="DespatchAdvice.Create"/> — `R36`, F6/F7/F8's creation
/// half (specs/shared/asyncapi.yaml <c>OrderDespatchedPayload</c>). Unlike
/// <see cref="StockReserved"/>/<see cref="StockReleased"/>, whose carrier is a
/// <see cref="StockItem"/> because no despatch exists yet at that point in
/// the saga, THIS fact's <see cref="StockDomainEvent.AggregateId"/> is the
/// <see cref="DespatchAdvice"/>'s own id — the despatch IS the aggregate that
/// produced it. <see cref="Lines"/> reuses <see cref="DespatchLine"/> from
/// <c>Contracts.Facts</c> directly, the same domain-event-may-reference-
/// Contracts-for-payload-record-types precedent <see cref="StockReserved"/>
/// already sets for <see cref="ReservationRef"/>.
/// </summary>
public sealed record OrderDespatched(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string DespatchReference,
    DateTimeOffset DespatchDate,
    string CompanyCode,
    string RetailerCode,
    IReadOnlyList<DespatchLine> Lines)
    : StockDomainEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "order.despatched.v1";
}
