using OrderToCash.Orders.Infrastructure.Messaging.Rpc;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>
/// The two correlation values every saga command carries on the wire
/// (<c>asyncapi.yaml</c>'s <c>RpcHeaders</c>): <see cref="CorrelationId"/> is
/// the order id (<c>x-correlation-id</c>) and <see cref="RequestId"/> is the
/// id of the durable <c>saga_commands</c> row being dispatched
/// (<c>x-request-id</c>). Both are stable across every in-line retry and
/// every sweeper re-issue of the same row — a retry reuses the same value,
/// which is what lets a responder recognise a duplicate (design.md §11,
/// `FS2`).
/// </summary>
public readonly record struct SagaCommandMeta(UniqueId CorrelationId, UniqueId RequestId);

/// <summary>
/// The five outbound saga commands over the RPC transport (design.md §6.1).
/// Request/reply payload records are transcribed from
/// <c>specs/shared/asyncapi.yaml</c> into
/// <c>Infrastructure/Messaging/Rpc/SagaCommandPayloads.cs</c>, referenced
/// directly here rather than duplicated behind a second, port-local DTO
/// shape — the same reuse this feature's design explicitly chooses (design.md
/// §6.1's own snippet). Every method now carries a <see cref="SagaCommandMeta"/>
/// (feature 17, `FS2`) so the responder on the other end can stamp
/// <c>correlationId</c>/<c>causationId</c> on any fact it emits (`R12`).
/// </summary>
public interface ISagaCommands
{
    Task<StockReserveReplyPayload> ReserveStockAsync(StockReserveRequestPayload request, SagaCommandMeta meta, CancellationToken cancellationToken);

    Task<StockReleaseReplyPayload> ReleaseStockAsync(StockReleaseRequestPayload request, SagaCommandMeta meta, CancellationToken cancellationToken);

    Task<DespatchCreateReplyPayload> CreateDespatchAsync(DespatchCreateRequestPayload request, SagaCommandMeta meta, CancellationToken cancellationToken);

    Task<CreditHoldReplyPayload> HoldCreditAsync(CreditHoldRequestPayload request, SagaCommandMeta meta, CancellationToken cancellationToken);

    Task<InvoiceIssueReplyPayload> IssueInvoiceAsync(InvoiceIssueRequestPayload request, SagaCommandMeta meta, CancellationToken cancellationToken);
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
