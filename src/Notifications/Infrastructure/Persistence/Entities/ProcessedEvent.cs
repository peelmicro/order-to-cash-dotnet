namespace OrderToCash.Notifications.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_notifications.processed_events` — the
/// Notifications service's only table (Databases doc §7). The service has
/// no aggregate: it consumes facts and sends emails through Mailtrap, and
/// this durable ledger is the whole point — it got its own database after a
/// consumer-group replay caused a real Mailtrap rate-limit storm. One row
/// per `(EventId, Consumer)`; append-only, no `UpdatedAt`. Shape copied
/// verbatim from
/// `OrderToCash.Orders.Infrastructure.Persistence.Entities.ProcessedEvent`
/// (§4.3, byte-identical across `otc_orders`, `otc_fulfillment`,
/// `otc_billing` and `otc_notifications`) — feature db_billing's
/// cross-context parity test guards this.
/// </summary>
public sealed class ProcessedEvent
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public string Consumer { get; set; } = string.Empty;

    public DateTime ProcessedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
