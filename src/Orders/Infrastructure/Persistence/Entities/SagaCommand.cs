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
}
