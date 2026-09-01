namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_fulfillment.processed_events` — the idempotent-
/// consumer ledger (Databases doc §4.3, byte-identical across `otc_orders`,
/// `otc_fulfillment` and `otc_billing`). One row per `(EventId, Consumer)`;
/// append-only, no `UpdatedAt`. Shape copied verbatim from
/// `OrderToCash.Orders.Infrastructure.Persistence.Entities.ProcessedEvent`.
/// </summary>
public sealed class ProcessedEvent
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public string Consumer { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
