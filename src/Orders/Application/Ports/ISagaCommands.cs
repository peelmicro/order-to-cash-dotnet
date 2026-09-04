using OrderToCash.Orders.Infrastructure.Messaging.Rpc;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// The five outbound saga commands over the RPC transport (design.md §6.1).
/// Request/reply payload records are transcribed from
/// <c>specs/shared/asyncapi.yaml</c> into
/// <c>Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs</c>, referenced
/// directly here rather than duplicated behind a second, port-local DTO
/// shape — the same reuse this feature's design explicitly chooses (design.md
/// §6.1's own snippet).
/// </summary>
public interface ISagaCommands
{
    Task<StockReserveReplyPayload> ReserveStockAsync(StockReserveRequestPayload request, CancellationToken cancellationToken);

    Task<StockReleaseReplyPayload> ReleaseStockAsync(StockReleaseRequestPayload request, CancellationToken cancellationToken);

    Task<DespatchCreateReplyPayload> CreateDespatchAsync(DespatchCreateRequestPayload request, CancellationToken cancellationToken);

    Task<CreditHoldReplyPayload> HoldCreditAsync(CreditHoldRequestPayload request, CancellationToken cancellationToken);

    Task<InvoiceIssueReplyPayload> IssueInvoiceAsync(InvoiceIssueRequestPayload request, CancellationToken cancellationToken);
}

/// <summary>
/// The caller observed no reply within its per-attempt deadline — the
/// transport-level counterpart of <c>saga.md</c>'s "a timeout is a
/// legitimate, handled answer", applied to a saga command (SO4). Retryable.
/// </summary>
public sealed class SagaCommandTimeoutError(string subject, int timeoutMs)
    : Exception($"{subject}: no reply within {timeoutMs}ms.")
{
    public string Subject { get; } = subject;

    public int TimeoutMs { get; } = timeoutMs;
}

/// <summary>
/// Any other GENUINELY RETRYABLE failure — no responder subscribed (NATS "no
/// responders"), or a reply body that is an <c>RpcError</c> whose <c>code</c>
/// is one of the transient/infra codes (<c>TIMEOUT</c>, <c>UNAVAILABLE</c>,
/// <c>INTERNAL_ERROR</c>) — design.md §6.1. Retryable (SO4). A terminal
/// business rejection (feature 42) is NOT one of these — see
/// <see cref="SagaCommandBusinessRejectionError"/>.
/// </summary>
/// <remarks>
/// Kept a DISTINCT type from <see cref="SagaCommandTimeoutError"/> —
/// feature 15's <c>StockCheckTimeoutError</c>/<c>StockCheckTransportError</c>
/// split, and the review defect (D1) that made keeping it worth a blocking
/// finding there.
/// </remarks>
public sealed class SagaCommandTransportError(string subject, string reason)
    : Exception($"{subject}: transport failure: {reason}")
{
    public string Subject { get; } = subject;
}

/// <summary>
/// A TERMINAL business rejection (feature 42
/// <c>orders_saga_terminal_rejection_classification</c>) — the responder
/// replied with an <c>RpcError</c> whose <c>code</c> is one of the closed-set
/// business-outcome codes (<c>VALIDATION_FAILED</c>, <c>NOT_FOUND</c>,
/// <c>CONFLICT</c>, <c>PRECONDITION_FAILED</c>, <c>ORDER_NOT_CANCELLABLE</c>,
/// <c>STOCK_UNAVAILABLE</c>, <c>INVOICE_NOT_PAYABLE</c>,
/// <c>PAYMENT_MISMATCH</c>, <c>DOMAIN_ERROR</c> — <c>specs/shared/asyncapi.yaml</c>'s
/// twelve-code <c>RpcError.code</c> enum minus the three transient/infra
/// codes). Retrying it can NEVER succeed — it is a definitive "no" from the
/// responder's own domain, not a transport hiccup — so
/// <see cref="Infrastructure.Saga.SagaCommandDispatcher"/> short-circuits
/// straight to the terminal <c>rejected</c> status instead of SO4's
/// retry/backoff loop.
/// </summary>
/// <remarks>
/// Distinct from SO6's existing "a business rejection is not an error" rule
/// (a TYPED reply payload's <c>outcome: rejected</c> for
/// <c>stock.reserve</c>/<c>credit.hold</c>, which resolves normally and is
/// marked <c>sent</c>): an <c>RpcError</c>-shaped reply means the command
/// itself was never fulfilled, so the row cannot be marked <c>sent</c> — it
/// is marked <c>rejected</c> instead, a status <c>ClaimDueAsync</c>'s
/// predicate structurally never re-claims.
/// </remarks>
public sealed class SagaCommandBusinessRejectionError(string subject, string rpcErrorCode, string reason)
    : Exception($"{subject}: terminal business rejection ({rpcErrorCode}): {reason}")
{
    public string Subject { get; } = subject;

    public string RpcErrorCode { get; } = rpcErrorCode;
}
