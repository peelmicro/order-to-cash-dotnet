using OrderToCash.Contracts.Facts;

namespace OrderToCash.Orders.Infrastructure.Messaging.Rpc;

// The five saga command request/reply payload records, transcribed from
// specs/shared/asyncapi.yaml §6.1's schema table
// (components.schemas.StockReserveRequestPayload and its nine siblings).
// Money is `long` minor units throughout — never `decimal` — and serialised
// through the existing RpcJson (the one shared JsonWire.Options), never a
// second JsonSerializerOptions. Reply line shapes (ReservationRef, Shortage,
// InvoiceLine) are reused from Contracts.Facts rather than re-declared — the
// wire shape is identical, and these RPC payloads are not themselves added
// to Contracts (design.md §6.1: "RPC payloads live in the service that
// speaks them").

// -- fulfillment.stock.reserve ----------------------------------------------

/// <summary>One requested line of <c>StockReserveRequestPayload.lines[]</c>.</summary>
public sealed record StockReserveRequestLine(string ProductCode, int Units);

/// <summary><c>asyncapi.yaml</c> <c>StockReserveRequestPayload</c>.</summary>
public sealed record StockReserveRequestPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    IReadOnlyList<StockReserveRequestLine> Lines);

/// <summary>
/// <c>asyncapi.yaml</c> <c>StockReserveReplyPayload</c>. <c>Outcome</c> is one
/// of <c>accepted</c> | <c>rejected</c> | <c>already_reserved</c> — a
/// business outcome resolved normally (SO6), never thrown.
/// <see cref="Reservations"/> is present when accepted/already_reserved;
/// <see cref="Shortages"/> when rejected.
/// </summary>
public sealed record StockReserveReplyPayload(
    string Outcome,
    string OrderReference,
    IReadOnlyList<ReservationRef>? Reservations = null,
    IReadOnlyList<Shortage>? Shortages = null);

// -- fulfillment.stock.release -----------------------------------------------

/// <summary><c>asyncapi.yaml</c> <c>StockReleaseRequestPayload</c>. <c>Reason</c> is <c>credit_rejected</c> | <c>order_cancelled</c>.</summary>
public sealed record StockReleaseRequestPayload(string OrderReference, string Reason);

/// <summary><c>asyncapi.yaml</c> <c>StockReleaseReplyPayload</c>. <c>Outcome</c> is <c>released</c> | <c>already_released</c> — both a plain success (SO6).</summary>
public sealed record StockReleaseReplyPayload(
    string Outcome,
    string OrderReference,
    IReadOnlyList<ReservationRef>? Released = null);

// -- fulfillment.despatch.create ---------------------------------------------

/// <summary><c>asyncapi.yaml</c> <c>DespatchCreateRequestPayload</c>.</summary>
public sealed record DespatchCreateRequestPayload(string OrderReference);

/// <summary><c>asyncapi.yaml</c> <c>DespatchCreateReplyPayload</c>. <c>Created</c> is <c>false</c> on the idempotent repeat (invariant F8) — still a plain success.</summary>
public sealed record DespatchCreateReplyPayload(
    string OrderReference,
    string DespatchReference,
    DateTimeOffset DespatchDate,
    bool Created,
    IReadOnlyList<DespatchLine>? Lines = null);

// -- billing.credit.hold -----------------------------------------------------

/// <summary>
/// <c>asyncapi.yaml</c>'s <c>Money</c> schema (<c>{ amount, currency }</c>) —
/// the one saga command payload that carries an amount travelling ALONE
/// (design.md §6.1), so it is nested rather than flattened the way every
/// fact payload's own total/currency pair already is
/// (<c>OrderFactPayloadMapper</c>'s own shape). Not
/// <c>OrderToCash.SharedKernel.Money</c> itself — that type's wire property
/// is <c>MinorUnits</c>, not <c>amount</c>, and the domain type must never
/// appear on this Infrastructure/-only wire seam.
/// </summary>
public sealed record SagaMoney(long Amount, string Currency);

/// <summary><c>asyncapi.yaml</c> <c>CreditHoldRequestPayload</c>.</summary>
public sealed record CreditHoldRequestPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    SagaMoney Amount);

/// <summary>
/// <c>asyncapi.yaml</c> <c>CreditHoldReplyPayload</c>. <c>Outcome</c> is
/// <c>approved</c> | <c>rejected</c> | <c>already_held</c> — a business
/// outcome resolved normally (SO6). <c>Reason</c> is present only when
/// rejected.
/// </summary>
public sealed record CreditHoldReplyPayload(
    string Outcome,
    string OrderReference,
    string Currency,
    long AvailableCredit,
    string? CreditCode = null,
    long? HeldAmount = null,
    string? Reason = null);

// -- billing.invoice.issue ---------------------------------------------------

/// <summary><c>asyncapi.yaml</c> <c>InvoiceIssueRequestPayload</c>. <c>Lines</c>/<c>Discount</c> are built from the aggregate, frozen since <c>confirmed</c> (R22).</summary>
public sealed record InvoiceIssueRequestPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<InvoiceLine> Lines,
    long? Discount = null);

/// <summary><c>asyncapi.yaml</c> <c>InvoiceIssueReplyPayload</c>. <c>Created</c> is <c>false</c> on the idempotent repeat (invariant B7) — still a plain success.</summary>
public sealed record InvoiceIssueReplyPayload(
    string OrderReference,
    string InvoiceReference,
    DateTimeOffset InvoiceDate,
    string Currency,
    long TotalAmount,
    string Status,
    bool Created,
    Guid? InvoiceId = null);
