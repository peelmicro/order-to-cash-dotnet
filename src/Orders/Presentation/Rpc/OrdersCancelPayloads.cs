namespace OrderToCash.Orders.Presentation.Rpc;

/// <summary>
/// <c>asyncapi.yaml</c> <c>OrdersCancelRequestPayload</c> — the
/// <c>orders.cancel</c> request body. <c>Reason</c> is the wire's own
/// <c>const</c> (<c>operator_cancelled</c>) — carried as a string here
/// (never a <see cref="Domain.CancellationReason"/>) so
/// <see cref="OrdersCancelRequestValidator"/> can reject anything else with
/// a client-caused refusal rather than a parse exception. <c>OrderReference</c>
/// is on the wire per the schema, but this responder locates the order by
/// <c>OrderId</c> only — the Gateway's <c>POST /orders/{id}/cancel</c> is the
/// only caller and always sends the id, matching #7's own
/// <c>orders-cancel.dto.ts</c> rationale.
/// </summary>
public sealed record OrdersCancelRequestPayload(Guid? OrderId, string? OrderReference, string? Reason, string? Note);

/// <summary>
/// <c>asyncapi.yaml</c> <c>OrdersCancelReplyPayload</c>. <c>CompensationPlanned</c>
/// is the schema's own REQUIRED field — always present, empty for the
/// immediate-cancel branch, never omitted. <c>CancellationReason</c> is
/// present only once the order has actually reached <c>cancelled</c>
/// (absent — omitted, not null — while compensation is still pending).
/// </summary>
public sealed record OrdersCancelReplyPayload(
    Guid OrderId,
    string OrderReference,
    string Status,
    IReadOnlyList<string> CompensationPlanned,
    string? CancellationReason = null);
