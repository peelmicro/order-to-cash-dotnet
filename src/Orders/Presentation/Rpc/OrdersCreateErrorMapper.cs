using OrderToCash.Orders.Application.Commands;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Presentation.Rpc;

/// <summary>
/// Translates every failure the <c>orders.create</c> responder can observe
/// — the application-layer errors of <c>PlaceOrderCommandHandler</c>, the
/// domain errors of the <c>Order</c> aggregate, the stock-check port's
/// transport errors, and anything unexpected — into the ONE wire shape
/// <c>asyncapi.yaml</c>'s <c>RpcError</c> schema names. Reproduces #7's
/// mapping (orders_aggregate design.md §9.2, itself citing
/// <c>apps/orders/src/presentation/rpc-error-mapper.ts:72-75</c>) rather
/// than inventing a finer-grained one — an RPC reply is on the wire, and the
/// same wire the API test script asserts against.
/// </summary>
public static class OrdersCreateErrorMapper
{
    public static RpcErrorPayload Map(Exception error, DateTimeOffset occurredAt) => error switch
    {
        // review A2 — a request that does not even satisfy the wire schema
        // (a required field missing or empty) is client-caused, checked
        // BEFORE every other case so it never falls through to the
        // catch-all as an INTERNAL_ERROR.
        InvalidOrdersCreateRequestError e => new RpcErrorPayload("VALIDATION_FAILED", e.Message, OccurredAt: occurredAt),

        StockUnavailableError e => new RpcErrorPayload(
            "STOCK_UNAVAILABLE",
            e.Message,
            new Dictionary<string, object?> { ["shortages"] = e.Shortages },
            OccurredAt: occurredAt),

        StockCheckTimeoutError e => new RpcErrorPayload(
            "TIMEOUT",
            e.Message,
            new Dictionary<string, object?> { ["subject"] = e.Subject, ["timeoutMs"] = e.TimeoutMs },
            OccurredAt: occurredAt),

        StockCheckTransportError e => new RpcErrorPayload(
            "UNAVAILABLE",
            e.Message,
            new Dictionary<string, object?> { ["subject"] = e.Subject },
            OccurredAt: occurredAt),

        ReferenceDataNotFoundError e => new RpcErrorPayload(
            "NOT_FOUND",
            e.Message,
            new Dictionary<string, object?> { ["field"] = e.Field, ["value"] = e.Value },
            OccurredAt: occurredAt),

        // Every OTHER application-layer refusal not special-cased above —
        // OrderDiscountNotSupportedError today — is client-caused, so it
        // collapses to VALIDATION_FAILED with no further details (matching
        // #7's own handling of the identical case).
        PlaceOrderError e => new RpcErrorPayload("VALIDATION_FAILED", e.Message, OccurredAt: occurredAt),

        // Every aggregate refusal is client-caused too — the request
        // described an order that violates an invariant. details.code
        // preserves the specific domain Code for a caller that wants it
        // (design.md §9.2: "The details key is code, not domainCode").
        DomainError e => new RpcErrorPayload(
            "VALIDATION_FAILED",
            e.Message,
            new Dictionary<string, object?> { ["code"] = e.Code },
            OccurredAt: occurredAt),

        _ => new RpcErrorPayload("INTERNAL_ERROR", error.Message, OccurredAt: occurredAt),
    };
}
