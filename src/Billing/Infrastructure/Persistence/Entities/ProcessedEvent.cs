namespace OrderToCash.Billing.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_billing.processed_events` — the idempotent-
/// consumer ledger (Databases doc §4.3, byte-identical across `otc_orders`,
/// `otc_fulfillment`, `otc_billing` and `otc_notifications` — feature
/// db_billing's cross-context parity test guards this). One row per
/// `(EventId, Consumer)`; append-only, no `UpdatedAt`. Shape copied verbatim
/// from
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
