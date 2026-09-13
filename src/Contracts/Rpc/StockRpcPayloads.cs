using OrderToCash.Contracts.Facts;

namespace OrderToCash.Contracts.Rpc;

// The sixteen request/reply payload records of the six fulfillment.stock.*
// subjects, transcribed from specs/shared/asyncapi.yaml. Feature 76
// (application_layer_depends_on_infrastructure_unguarded) moved these here
// from src/Fulfillment/Infrastructure/Messaging/Rpc/StockRpcPayloads.cs AND
// unified StockCheckRequestLine/StockCheckRequestPayload/StockCheckReplyLine/
// StockCheckReplyPayload/StockReserveRequestLine/StockReserveRequestPayload/
// StockReserveReplyPayload/StockReleaseRequestPayload/StockReleaseReplyPayload
// with Orders' own caller-side copy
// (src/Orders/Infrastructure/Messaging/Rpc/StockCheckPayloads.cs and the
// stock section of SagaCommandPayloads.cs), which declared structurally
// IDENTICAL types for the same subjects — see CreditRpcPayloads.cs's own
// header for the full reasoning. Money never appears here — this service
// handles no money at all.

// -- fulfillment.stock.check -------------------------------------------------

/// <summary>One line of <c>StockCheckRequestPayload.lines[]</c>.</summary>
public sealed record StockCheckRequestLine(string ProductCode, int Quantity);

/// <summary><c>asyncapi.yaml</c> <c>StockCheckRequestPayload</c>.</summary>
public sealed record StockCheckRequestPayload(string CompanyCode, IReadOnlyList<StockCheckRequestLine> Lines);

/// <summary>One line of <c>StockCheckReplyPayload.lines[]</c>.</summary>
public sealed record StockCheckReplyLine(string ProductCode, int Requested, int Available, bool Sufficient);

/// <summary><c>asyncapi.yaml</c> <c>StockCheckReplyPayload</c> — `R31`, `FS22`. An unknown product answers a line with <c>available: 0, sufficient: false</c>, never an <c>RpcError</c>.</summary>
public sealed record StockCheckReplyPayload(bool Available, IReadOnlyList<StockCheckReplyLine> Lines);

// -- fulfillment.stock.reserve ------------------------------------------------

/// <summary>One requested line of <c>StockReserveRequestPayload.lines[]</c>.</summary>
public sealed record StockReserveRequestLine(string ProductCode, int Units);

/// <summary><c>asyncapi.yaml</c> <c>StockReserveRequestPayload</c>.</summary>
public sealed record StockReserveRequestPayload(string OrderReference, string RetailerCode, string CompanyCode, IReadOnlyList<StockReserveRequestLine> Lines);

/// <summary><c>asyncapi.yaml</c> <c>StockReserveReplyPayload</c>. <c>Outcome</c> is <c>accepted</c> | <c>rejected</c> | <c>already_reserved</c> — a business outcome resolved normally (SO6), never thrown.</summary>
public sealed record StockReserveReplyPayload(
    string Outcome,
    string OrderReference,
    IReadOnlyList<ReservationRef>? Reservations = null,
    IReadOnlyList<Shortage>? Shortages = null);

// -- fulfillment.stock.release ------------------------------------------------

/// <summary><c>asyncapi.yaml</c> <c>StockReleaseRequestPayload</c>. <c>Reason</c> is <c>credit_rejected</c> | <c>order_cancelled</c>.</summary>
public sealed record StockReleaseRequestPayload(string OrderReference, string Reason);

/// <summary><c>asyncapi.yaml</c> <c>StockReleaseReplyPayload</c>. <c>Outcome</c> is <c>released</c> | <c>already_released</c> — both a plain success (SO6).</summary>
public sealed record StockReleaseReplyPayload(string Outcome, string OrderReference, IReadOnlyList<ReservationRef>? Released = null);

// -- fulfillment.stock.list ---------------------------------------------------

/// <summary><c>asyncapi.yaml</c> <c>PageInfo</c>.</summary>
public sealed record StockPageInfo(int Page, int PageSize, int Total);

/// <summary><c>asyncapi.yaml</c> <c>StockListRequestPayload</c> — <c>PageRequest</c> flattened onto it, per the schema's own <c>allOf</c>.</summary>
public sealed record StockListRequestPayload(
    int? Page,
    int? PageSize,
    string? CompanyCode = null,
    string? ProductCode = null,
    bool? BelowThreshold = null);

/// <summary><c>asyncapi.yaml</c> <c>StockView</c>.</summary>
public sealed record StockViewPayload(string CompanyCode, string ProductCode, int Units, int ReservedUnits, int AvailableUnits, int LowStockThreshold);

/// <summary><c>asyncapi.yaml</c> <c>StockListReplyPayload</c>.</summary>
public sealed record StockListReplyPayload(IReadOnlyList<StockViewPayload> Items, StockPageInfo Page);

// -- fulfillment.stock.replenish ----------------------------------------------

/// <summary>One line of <c>StockReplenishRequestPayload.lines[]</c> — a DELTA to add to on-hand stock, never a target level.</summary>
public sealed record StockReplenishRequestLine(string ProductCode, int Units);

/// <summary><c>asyncapi.yaml</c> <c>StockReplenishRequestPayload</c>.</summary>
public sealed record StockReplenishRequestPayload(string CompanyCode, IReadOnlyList<StockReplenishRequestLine> Lines);

/// <summary><c>asyncapi.yaml</c> <c>StockReplenishReplyPayload</c> — the affected items after replenishment.</summary>
public sealed record StockReplenishReplyPayload(IReadOnlyList<StockViewPayload> Items);
