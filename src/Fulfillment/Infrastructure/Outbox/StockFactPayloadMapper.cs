using OrderToCash.Fulfillment.Domain.Events;
using ContractsPayloads = OrderToCash.Contracts.Facts.Payloads;

namespace OrderToCash.Fulfillment.Infrastructure.Outbox;

/// <summary>
/// The ONE place a domain event becomes an
/// <c>OrderToCash.Contracts.Facts.Payloads.*</c> record — the
/// <c>OrderFactPayloadMapper</c> shape (design.md §7.1). The domain carries
/// domain types (<see cref="OrderToCash.SharedKernel.OrderNumber"/>) and must
/// never reference <c>Contracts</c> for the ENVELOPE; this mapper lives in
/// <c>Infrastructure/Outbox/</c>.
/// </summary>
public static class StockFactPayloadMapper
{
    public static object ToPayload(StockDomainEvent domainEvent) => domainEvent switch
    {
        StockReserved reserved => new ContractsPayloads.StockReservedPayload(
            reserved.OrderReference.Value,
            reserved.CompanyCode,
            reserved.Reservations,
            reserved.RetailerCode),
        StockRejected rejected => new ContractsPayloads.StockRejectedPayload(
            rejected.OrderReference.Value,
            rejected.CompanyCode,
            rejected.Shortages,
            rejected.Reason,
            rejected.RetailerCode),
        StockReleased released => new ContractsPayloads.StockReleasedPayload(
            released.OrderReference.Value,
            released.CompanyCode,
            released.Released,
            released.Reason,
            released.RetailerCode),
        _ => throw new InvalidOperationException($"StockFactPayloadMapper has no mapping for event type '{domainEvent.GetType().FullName}' (eventType '{domainEvent.EventType}')."),
    };
}
