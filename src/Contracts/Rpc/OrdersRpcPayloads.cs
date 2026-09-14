namespace OrderToCash.Contracts.Rpc;

// The ten request/reply payload records of the orders.create and
// orders.cancel subjects, transcribed from specs/shared/asyncapi.yaml.
// Backlog id 93 (orders_and_catalog_rpc_payloads_are_still_duplicated_
// between_orders_and_the_gateway) moved these here from
// src/Orders/Presentation/Rpc/OrdersCreatePayloads.cs and
// OrdersCancelPayloads.cs AND unified them with the Gateway's own
// caller-side copy (src/Gateway/Application/Rpc/GatewayRpcPayloads.cs's
// orders.create/orders.cancel sections, which declared structurally
// IDENTICAL types for the same subjects) — see CreditRpcPayloads.cs's own
// header for the full reasoning feature 76 established and id 84 extended
// to the Gateway. Until this entry, orders.create/orders.cancel were the
// two subjects feature 76/id 84 deliberately left out (id 84's own header
// called moving Orders' Presentation records "a different change against a
// different feature's design"); this entry is that change.
//
// PORTED-IDIOM LEDGER — #7 has no such duplication because its payload
// types are GENERATED into packages/contracts/src/generated/ and
// drift-checked by packages/contracts/scripts/check.mjs and check.spec.ts,
// consumed directly at apps/fulfillment/src/application/ports/
// stock-read.port.ts:3 and apps/gateway/src/application/queries/
// list-stock.query.ts:9. #8 hand-writes its contract types, so the
// property #7 gets from code generation is supplied here by nothing, and
// duplication was the observable consequence. Guard:
// tests/Gateway.UnitTests/GatewayRpcPayloadTests.cs's
// GatewayPayload_CarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped
// and GatewayPayload_EveryRowsSchemaNameNamesTheRecordThatRowClaims theory
// rows already cover every record in this file (they did before the move
// too, reading OrderToCash.Gateway.Application.Rpc's own then-local
// copies) — an undeclared property or a substituted sibling schema name is
// caught the same way GatewayRpcPayloadTests already caught it for the six
// subjects id 84 unified, by reading the record's own properties rather
// than a second hand-typed key list. Armed for id 93: adding an undeclared
// property to OrdersCreateReplyPayload failed
// GatewayPayload_CarriesExactlyThePropertyNamesAsyncApiDeclares_ParsedFromTheSpecNeverRetyped
// naming the property; see progress/impl_phase15_batch2_payload_
// unification_and_offset_guards.md for the verbatim arming table.

// -- orders.create ------------------------------------------------------

/// <summary><c>asyncapi.yaml</c> <c>OrdersCreateRequestPayload.lines[]</c>.</summary>
public sealed record OrdersCreateRequestLine(string ProductCode, int Quantity, long? UnitPrice, long? LineDiscount);

/// <summary>
/// <c>asyncapi.yaml</c> <c>OrdersCreateRequestPayload</c> — the
/// <c>orders.create</c> request body. <see cref="RequestId"/> is read by
/// Orders' responder and carried through to <c>PlaceOrderCommand</c>, whose
/// handler realises feature <c>observability_reliability</c>'s
/// <c>RI1</c>–<c>RI5</c> idempotent-replay behaviour (design.md §2).
/// </summary>
public sealed record OrdersCreateRequestPayload(
    Guid? RequestId,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<OrdersCreateRequestLine> Lines,
    long? OrderDiscount,
    string? Notes);

/// <summary><c>asyncapi.yaml</c> <c>OrdersCreateReplyPayload</c> — the <c>orders.create</c> success reply body. <c>Status</c> is always the literal <c>"placed"</c> (the schema's own <c>const</c>).</summary>
public sealed record OrdersCreateReplyPayload(
    Guid OrderId,
    string OrderReference,
    string Status,
    string Currency,
    long InitialAmount,
    long InitialDiscount,
    long TotalAmount,
    DateTimeOffset OrderDate);

// -- orders.cancel --------------------------------------------------------

/// <summary>
/// <c>asyncapi.yaml</c> <c>OrdersCancelRequestPayload</c> — the
/// <c>orders.cancel</c> request body. <c>Reason</c> is the wire's own
/// <c>const</c> (<c>operator_cancelled</c>) — carried as a string here
/// (never a domain <c>CancellationReason</c>) so Orders' own
/// <c>OrdersCancelRequestValidator</c> can reject anything else with a
/// client-caused refusal rather than a parse exception. <c>OrderReference</c>
/// is on the wire per the schema, but Orders' responder locates the order by
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
/// (absent — omitted, not null — while compensation is still pending). The
/// default on <c>CancellationReason</c> is what Orders'
/// <c>OrdersCancelPayloadTests</c> relies on to construct a confirmed-branch
/// reply without naming the argument.
/// </summary>
public sealed record OrdersCancelReplyPayload(
    Guid OrderId,
    string OrderReference,
    string Status,
    IReadOnlyList<string> CompensationPlanned,
    string? CancellationReason = null);
