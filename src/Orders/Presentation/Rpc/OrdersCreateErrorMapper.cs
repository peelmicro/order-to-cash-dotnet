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
    /// <summary>
    /// <c>asyncapi.yaml</c> <c>components.schemas.RpcError.code</c>'s closed
    /// twelve-value enum, mirrored here so this mapper can validate BEFORE
    /// writing to the wire — the same discipline
    /// <c>NatsSagaCommandsAdapter.IsTerminalRpcErrorCode</c> already applies
    /// one file over (review D2, round 2): an external string must not
    /// reach a wire enum field unvalidated.
    /// </summary>
    private static readonly HashSet<string> _contractRpcErrorCodes =
    [
        "VALIDATION_FAILED", "NOT_FOUND", "CONFLICT", "PRECONDITION_FAILED",
        "ORDER_NOT_CANCELLABLE", "STOCK_UNAVAILABLE", "INVOICE_NOT_PAYABLE",
        "PAYMENT_MISMATCH", "DOMAIN_ERROR", "INTERNAL_ERROR", "UNAVAILABLE", "TIMEOUT",
    ];

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

        // Feature 46: the responder answered with its OWN RpcError — code
        // and message pass through unchanged rather than collapsing to
        // INTERNAL_ERROR (the bug this case fixes; see
        // NatsStockAvailabilityChecker's discriminator). Review D2 (round
        // 2): the pass-through is only safe while the responder's code is
        // itself one of the twelve the wire enum permits, so it is clamped
        // below rather than forwarded raw.
        StockCheckBusinessError e => MapStockCheckBusinessError(e, occurredAt),

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

    /// <summary>
    /// Review D2 (round 2), the ported-idiom ledger's own form: #7's
    /// <c>nats-stock-availability.adapter.ts:98-99</c> collapses EVERY
    /// <c>RpcError</c>-shaped reply to <c>StockCheckTransportError</c>,
    /// which <c>rpc-error-mapper.ts:52-58</c> maps to <c>UNAVAILABLE</c> —
    /// so an out-of-enum code reaching #7's own <c>orders.create</c> wire
    /// was structurally impossible there. #8 forwards the responder's own
    /// code instead, for the sharper terminal-versus-transient signal that
    /// gives the caller, so here that same impossibility is supplied by
    /// this clamp: a code the responder sent that is not one of
    /// <see cref="_contractRpcErrorCodes"/>'s twelve values is never written
    /// to the wire's <c>code</c> field. It falls back to
    /// <c>UNAVAILABLE</c> — #7's own choice for the identical
    /// "responder answered something this side does not recognise" case —
    /// while the responder's original code stays visible, unlike #7, in
    /// <c>details.responderCode</c>, and the responder's own message is
    /// still the wire message either way (that half of "visible" needed no
    /// clamp: <see cref="StockCheckBusinessError.ResponderMessage"/> was
    /// never enum-constrained).
    /// </summary>
    private static RpcErrorPayload MapStockCheckBusinessError(StockCheckBusinessError e, DateTimeOffset occurredAt)
    {
        var details = new Dictionary<string, object?> { ["subject"] = e.Subject };
        var code = e.RpcErrorCode;

        if (!_contractRpcErrorCodes.Contains(code))
        {
            details["responderCode"] = code;
            code = "UNAVAILABLE";
        }

        return new RpcErrorPayload(code, e.ResponderMessage, details, OccurredAt: occurredAt);
    }
}
