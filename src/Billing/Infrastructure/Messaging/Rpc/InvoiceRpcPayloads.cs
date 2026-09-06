using OrderToCash.Contracts.Facts;

namespace OrderToCash.Billing.Infrastructure.Messaging.Rpc;

// The seven request/reply payload records of the two billing.invoice.*
// subjects, transcribed from specs/shared/asyncapi.yaml — Billing's OWN
// copy, not a reference to Orders' SagaCommandPayloads.cs, per the
// established rule that "RPC payloads live in the service that speaks
// them" (design.md §4.3). `BI28`'s parsed-from-the-spec test is what makes
// following the generated contract mechanical rather than a matter of care.
// Every optional property is nullable, so an absent value is OMITTED,
// never sent as `null` (`JsonWire.Options`).

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
