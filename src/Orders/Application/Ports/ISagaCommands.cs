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
/// Any other transport-level failure — no responder subscribed (NATS "no
/// responders"), or a reply body that is an <c>RpcError</c> (design.md §6.1's
/// pre-42 taxonomy: every <c>RpcError</c> reply is classified retryable here,
/// deliberately). Retryable (SO4).
/// </summary>
/// <remarks>
/// Kept a DISTINCT type from <see cref="SagaCommandTimeoutError"/> —
/// feature 15's <c>StockCheckTimeoutError</c>/<c>StockCheckTransportError</c>
/// split, and the review defect (D1) that made keeping it worth a blocking
/// finding there. <b>Feature 42</b> (<c>orders_saga_terminal_rejection_classification</c>)
/// owns the next refinement: splitting the <c>RpcError</c>-body row on its
/// <c>code</c> into a terminal-business set and a transient set. This
/// feature must not pre-empt that classification.
/// </remarks>
public sealed class SagaCommandTransportError(string subject, string reason)
    : Exception($"{subject}: transport failure: {reason}")
{
    public string Subject { get; } = subject;
}
