using OrderToCash.Orders.Application.Sagas;

namespace OrderToCash.Orders.Application.Ports;

/// <summary>Whether an enqueue actually inserted a new row, or found the command already owed/sent (design.md §6.3 — a duplicate-key hit on the unique <c>(order_id, command)</c> index).</summary>
public enum EnqueueOutcome
{
    Enqueued,
    AlreadyEnqueued,
}

/// <summary>The durable row's shape a claim hands the caller — everything <see cref="ISagaCommands"/> and the retry policy need, with no second read.</summary>
public sealed record SagaCommandRecord(
    Guid Id,
    Guid OrderId,
    string OrderReference,
    SagaCommandKind Command,
    string Payload,
    Guid TriggeringEventId,
    int Attempts);

/// <summary>
/// The durable <c>saga_commands</c> queue (design.md §6.3) — enqueue, claim,
/// claim-due, mark-sent, park. No <c>tx</c> parameter anywhere: the ambient
/// transaction comes from the caller's DI scope (feature 14, §2.1), exactly
/// as <see cref="IOrderRepository"/> already does.
/// </summary>
public interface ISagaCommandStore
{
    /// <summary>Inserts a <c>pending</c> row inside the caller's ambient transaction. A duplicate-key hit on <c>(order_id, command)</c> is not an error — it means the command is already owed or already sent (SO3).</summary>
    Task<EnqueueOutcome> EnqueueAsync(
        Guid orderId,
        string orderReference,
        SagaCommandKind command,
        string payload,
        Guid triggeringEventId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Claims exactly one row by <c>(orderId, command)</c> for dispatch,
    /// under a bounded lease (SO11) — a conditional <c>UPDATE</c> that
    /// affects at most one row. Returns <see langword="null"/> when it
    /// affects zero rows: a stale signal, a row already <c>sent</c>, or one
    /// currently held by a concurrent claimant/the sweeper — a SILENT no-op
    /// (design.md §6.2), never an error.
    /// </summary>
    Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken);

    /// <summary>The sweeper's batch claim (design.md §6.4): every stale <c>pending</c> row (SO3's crash window) and every due <c>parked</c> row (SO5), up to <paramref name="batchSize"/>, each under the same bounded lease as <see cref="TryClaimAsync"/>.</summary>
    Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>Marks a claimed row <c>sent</c> — a reply was delivered, including a business rejection (SO6). Never means "the saga advanced".</summary>
    Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken);

    /// <summary>Marks a claimed row <c>parked</c> on exhaustion (SO5): accumulates <see cref="SagaCommandRecord.Attempts"/>, records the last error, and schedules the next retry on capped backoff.</summary>
    Task ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken);
}
