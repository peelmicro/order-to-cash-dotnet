namespace OrderToCash.Orders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_orders.processed_events` — the idempotent-
/// consumer ledger (Databases doc §4.3). One row per `(EventId, Consumer)`;
/// append-only, no `UpdatedAt`.
/// </summary>
public sealed class ProcessedEvent
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public string Consumer { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
