namespace OrderToCash.Billing.Infrastructure.Messaging.Rpc;

// The six request/reply payload records of the three billing.credit.*
// subjects, transcribed from specs/shared/asyncapi.yaml — Billing's OWN
// copy, not a reference to Orders' SagaCommandPayloads.cs, per the
// established rule that "RPC payloads live in the service that speaks
// them" (design.md §4.3). BC23's parsed-from-the-spec test is what makes
// following the generated contract mechanical rather than a matter of care.

// -- billing.credit.hold -----------------------------------------------------

/// <summary>The nested <c>{ amount, currency }</c> object <c>asyncapi.yaml</c>'s <c>Money</c> schema declares — a request field that travels alone, not a flattened pair (design.md §4.3).</summary>
public sealed record CreditMoney(long Amount, string Currency);

/// <summary><c>asyncapi.yaml</c> <c>CreditHoldRequestPayload</c>.</summary>
public sealed record CreditHoldRequestPayload(string OrderReference, string RetailerCode, string CompanyCode, CreditMoney Amount);

/// <summary><c>asyncapi.yaml</c> <c>CreditHoldReplyPayload</c>. <c>Outcome</c> is <c>approved</c> | <c>rejected</c> | <c>already_held</c>. Every optional property is nullable so an absent value is OMITTED, never sent as <c>null</c> (`JsonWire.Options`).</summary>
public sealed record CreditHoldReplyPayload(
    string Outcome,
    string OrderReference,
    string Currency,
    long AvailableCredit,
    string? CreditCode = null,
    long? HeldAmount = null,
    string? Reason = null);

// -- billing.credit.release ---------------------------------------------------

/// <summary><c>asyncapi.yaml</c> <c>CreditReleaseRequestPayload</c>. No <c>reason</c> field — this subject always releases with reason <c>order_cancelled</c> (`BC25`).</summary>
public sealed record CreditReleaseRequestPayload(string OrderReference, string RetailerCode, string CompanyCode);

/// <summary><c>asyncapi.yaml</c> <c>CreditReleaseReplyPayload</c>. <c>Released: false</c> is a plain success — an idempotent repeat or nothing was ever held (`BC11`, `B5`).</summary>
public sealed record CreditReleaseReplyPayload(
    bool Released,
    string OrderReference,
    long AvailableCreditAfter,
    string? CreditCode = null,
    string? Currency = null,
    long? ReleasedAmount = null);

// -- billing.credit.list -------------------------------------------------------

/// <summary><c>asyncapi.yaml</c> <c>PageInfo</c>.</summary>
public sealed record CreditPageInfo(int Page, int PageSize, int Total);

/// <summary><c>asyncapi.yaml</c> <c>CreditListRequestPayload</c> — <c>PageRequest</c> flattened onto it, per the schema's own <c>allOf</c>.</summary>
public sealed record CreditListRequestPayload(int? Page, int? PageSize, string? RetailerCode = null, string? CompanyCode = null);

/// <summary><c>asyncapi.yaml</c> <c>CreditView</c> — one credit line: limit, active holds, open invoice exposure, what is left.</summary>
public sealed record CreditViewPayload(
    string CreditCode,
    string RetailerCode,
    string CompanyCode,
    string Currency,
    long CreditLimit,
    long ActiveHolds,
    long OpenExposure,
    long AvailableCredit);

/// <summary><c>asyncapi.yaml</c> <c>CreditListReplyPayload</c>.</summary>
public sealed record CreditListReplyPayload(IReadOnlyList<CreditViewPayload> Items, CreditPageInfo Page);
