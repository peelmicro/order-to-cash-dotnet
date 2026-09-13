using OrderToCash.Contracts.Facts;

namespace OrderToCash.Contracts.Rpc;

// The nine request/reply payload records of the two billing.invoice.*
// subjects plus billing.payment.register, transcribed from
// specs/shared/asyncapi.yaml. Feature 76
// (application_layer_depends_on_infrastructure_unguarded) moved these here
// from src/Billing/Infrastructure/Messaging/Rpc/InvoiceRpcPayloads.cs AND
// unified InvoiceIssueRequestPayload/InvoiceIssueReplyPayload with Orders'
// own saga-side copy in
// src/Orders/Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs, which
// declared the structurally IDENTICAL pair for the same subject — see
// CreditRpcPayloads.cs's own header for the full reasoning. Every optional
// property is nullable, so an absent value is OMITTED, never sent as
// <c>null</c> (`JsonWire.Options`).

// -- billing.invoice.issue ----------------------------------------------------

/// <summary><c>asyncapi.yaml</c> <c>InvoiceIssueRequestPayload</c>. Reuses <c>Contracts.Facts.InvoiceLine</c> for <c>lines</c> — it is already the spec's own <c>InvoiceLine</c> shape.</summary>
public sealed record InvoiceIssueRequestPayload(
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    IReadOnlyList<InvoiceLine> Lines,
    long? Discount = null);

/// <summary><c>asyncapi.yaml</c> <c>InvoiceIssueReplyPayload</c>. <c>InvoiceId</c> is nullable so it can be omitted rather than sent as <c>null</c> — never populated today (the spec declares it optional, unused by this feature's own callers).</summary>
public sealed record InvoiceIssueReplyPayload(
    string OrderReference,
    string InvoiceReference,
    DateTimeOffset InvoiceDate,
    string Currency,
    long TotalAmount,
    string Status,
    bool Created,
    Guid? InvoiceId = null);

// -- billing.invoice.list -------------------------------------------------------

/// <summary><c>asyncapi.yaml</c> <c>PageInfo</c>.</summary>
public sealed record InvoicePageInfo(int Page, int PageSize, int Total);

/// <summary><c>asyncapi.yaml</c> <c>InvoiceListRequestPayload</c> — <c>PageRequest</c> flattened onto it, per the schema's own <c>allOf</c>.</summary>
public sealed record InvoiceListRequestPayload(
    int? Page,
    int? PageSize,
    string? Status = null,
    string? RetailerCode = null,
    string? CompanyCode = null,
    string? OrderReference = null,
    int? IssuedBeforeMinutes = null);

/// <summary>
/// <c>asyncapi.yaml</c> <c>InvoiceView</c>. <see cref="PaidAt"/> is nullable
/// so an `issued` invoice OMITS the key entirely rather than emitting
/// `null` — the ratified nulls-omitted rule (`CLAUDE.md`), and this
/// feature's one recorded divergence from #7's bytes (design.md §4.3, §16
/// ledger `L19`; `BI28`).
/// </summary>
public sealed record InvoiceViewPayload(
    Guid InvoiceId,
    string InvoiceReference,
    DateTimeOffset InvoiceDate,
    string OrderReference,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    long Amount,
    long Discount,
    long TotalAmount,
    string Status,
    DateTimeOffset? PaidAt = null);

/// <summary><c>asyncapi.yaml</c> <c>InvoiceListReplyPayload</c>.</summary>
public sealed record InvoiceListReplyPayload(IReadOnlyList<InvoiceViewPayload> Items, InvoicePageInfo Page);

// -- billing.payment.register ------------------------------------------------

/// <summary>
/// <c>asyncapi.yaml</c> <c>PaymentRegisterRequestPayload</c>. Exactly ONE of
/// <see cref="InvoiceId"/>/<see cref="InvoiceReference"/> is REQUIRED —
/// neither is in the schema's own `required:` list, so the cross-field
/// "at least one identifier" rule lives in <c>PaymentRegisterRequestValidator</c>,
/// not here (the placement <c>InvoiceRequestValidator</c>'s own
/// `discount` cross-field check already established).
/// </summary>
public sealed record PaymentRegisterRequestPayload(
    string PaymentReference,
    CreditMoney Amount,
    DateTimeOffset ValueDate,
    string Source,
    Guid? InvoiceId = null,
    string? InvoiceReference = null);

/// <summary>
/// <c>asyncapi.yaml</c> <c>PaymentRegisterReplyPayload</c>. <c>Outcome</c>
/// is <c>accepted</c> | <c>duplicate</c> — a mismatched amount/currency is
/// NOT a duplicate, it is an error reply (`R49`). The schema marks
/// <c>orderReference</c>/<c>paidAt</c> optional, but this responder always
/// knows both (a payment is only ever registered against an invoice this
/// service already holds), so both are always populated — never omitted in
/// practice, unlike <see cref="InvoiceIssueReplyPayload.InvoiceId"/>.
/// </summary>
public sealed record PaymentRegisterReplyPayload(
    string Outcome,
    string PaymentReference,
    string InvoiceReference,
    string OrderReference,
    string InvoiceStatus,
    DateTimeOffset? PaidAt = null);
