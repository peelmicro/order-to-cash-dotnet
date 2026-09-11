namespace OrderToCash.Orders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_orders.saga_commands` — the saga orchestrator's
/// durable command queue (Databases doc §4.4). Enqueued `pending` in the
/// same transaction as the fact that owed it; an in-line dispatcher sends it
/// over NATS (`sent`), and a background sweeper re-issues stale `pending`
/// rows and retries `parked` ones. The unique `(OrderId, Command)` index
/// guarantees a step can never owe the same command twice.
/// </summary>
public sealed class SagaCommand
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public string OrderReference { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    /// <summary>Full typed RPC request, snapshotted at enqueue time. Stored as `nvarchar(max)` — no `json` column type in MS-SQL.</summary>
    public string Payload { get; set; } = string.Empty;

    public Guid TriggeringEventId { get; set; }

    public string Status { get; set; } = "pending";

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    public DateTime? NextAttemptAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? SentAt { get; set; }

    /// <summary>
    /// Feature <c>observability_reliability</c> (design.md §4.1) — the raw
    /// UTF-8 bytes of the fact that owed this command, decoded to a string
    /// for the <c>nvarchar(max)</c> column and re-encoded on read
    /// (<see cref="Saga.EfCoreSagaCommandStore"/>). <see langword="null"/>
    /// for a row enqueued before this feature's columns existed, or for any
    /// other enqueue site that genuinely supplies no envelope (none exists
    /// today). An RPC-triggered compensation row (an operator cancel,
    /// <c>CancelOrderCommandHandler</c>) is NOT such a row: since feature
    /// <c>operator_note_survives_the_compensation_branches</c> (id 71) it
    /// carries a synthetic <c>orders.cancel.requested</c> envelope here,
    /// written exactly once, by the enqueue's own <c>INSERT</c>.
    /// </summary>
    public string? TriggeringEventEnvelope { get; set; }

    /// <summary>The source topic <see cref="TriggeringEventEnvelope"/> was consumed from — never re-derived from <see cref="Command"/>.</summary>
    public string? TriggeringEventTopic { get; set; }

    /// <summary><c>OR3</c>'s at-most-once marker (design.md §4.3) — set exactly once, by <see cref="Saga.EfCoreSagaCommandStore.TryClaimDeadLetterAsync"/>'s single conditional <c>UPDATE</c>.</summary>
    public DateTime? DeadLetteredAt { get; set; }
}
