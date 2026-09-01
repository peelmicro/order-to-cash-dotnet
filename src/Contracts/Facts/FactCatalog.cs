using OrderToCash.Contracts.Facts.Payloads;

namespace OrderToCash.Contracts.Facts;

/// <summary>
/// The registry a completeness test can walk: every fact <c>eventType</c>
/// declared in specs/shared/asyncapi.yaml (the fourteen `const` values under
/// `components.schemas.*Event.properties.eventType`) mapped to the payload
/// CLR type that represents it. This is the single place a fifteenth fact
/// would have to be added — a completeness test that enumerates the spec and
/// compares its keys against this dictionary's keys therefore fails loudly
/// if a spec-declared fact has no representing entry, and fails loudly again
/// if this dictionary drifts ahead of the spec.
/// </summary>
public static class FactCatalog
{
    public static readonly IReadOnlyDictionary<string, Type> PayloadTypesByEventType =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            ["order.placed.v1"] = typeof(OrderPlacedPayload),
            ["stock.rejected.v1"] = typeof(StockRejectedPayload),
            ["stock.reserved.v1"] = typeof(StockReservedPayload),
            ["stock.released.v1"] = typeof(StockReleasedPayload),
            ["credit.approved.v1"] = typeof(CreditApprovedPayload),
            ["credit.rejected.v1"] = typeof(CreditRejectedPayload),
            ["credit.released.v1"] = typeof(CreditReleasedPayload),
            ["order.confirmed.v1"] = typeof(OrderConfirmedPayload),
            ["order.despatched.v1"] = typeof(OrderDespatchedPayload),
            ["invoice.issued.v1"] = typeof(InvoiceIssuedPayload),
            ["payment.received.v1"] = typeof(PaymentReceivedPayload),
            ["order.completed.v1"] = typeof(OrderCompletedPayload),
            ["order.cancelled.v1"] = typeof(OrderCancelledPayload),
            ["order.saga_failed.v1"] = typeof(OrderSagaFailedPayload),
        };
}
