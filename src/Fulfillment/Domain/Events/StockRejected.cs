using OrderToCash.Contracts.Facts;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Domain.Events;

/// <summary>
/// Raised by <see cref="OrderStockReservation.Reserve"/> when at least one
/// line is short (`R33`, F3 — nothing was reserved;
/// specs/shared/asyncapi.yaml <c>StockRejectedPayload</c>).
/// <see cref="Reason"/> is <c>unknown_product</c> when any line names a
/// product the company does not stock (`FS8`), else <c>insufficient_stock</c>.
/// </summary>
public sealed record StockRejected(
    UniqueId EventId,
    UniqueId AggregateId,
    UniqueId CorrelationId,
    UniqueId CausationId,
    DateTimeOffset OccurredAt,
    OrderNumber OrderReference,
    string CompanyCode,
    string? RetailerCode,
    IReadOnlyList<Shortage> Shortages,
    string Reason)
    : StockDomainEvent(EventId, AggregateId, CorrelationId, CausationId, OccurredAt)
{
    public override string EventType => "stock.rejected.v1";
}
