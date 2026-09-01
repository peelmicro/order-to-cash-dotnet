namespace OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_fulfillment.outbox` — the transactional outbox
/// (Databases doc §4.3, byte-identical across `otc_orders`, `otc_fulfillment`
/// and `otc_billing`). Every domain fact is inserted here in the same
/// transaction as the aggregate change that produced it; a relay process
/// polls unpublished rows (`PublishedAt IS NULL`, ordered by <see
/// cref="Seq"/>), publishes them to Kafka and stamps <see
/// cref="PublishedAt"/> only after the broker acknowledges. Shape copied
/// verbatim from `OrderToCash.Orders.Infrastructure.Persistence.Entities.OutboxMessage`
/// (feature db_orders) rather than re-derived, per this feature's task
/// instructions and so a future cross-context parity test (feature
/// db_billing) has an easy job.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public Guid AggregateId { get; set; }

    public Guid CorrelationId { get; set; }

    public Guid CausationId { get; set; }

    /// <summary>The fact payload, as defined in `specs/shared/asyncapi.yaml`. Stored as `nvarchar(max)` — MS-SQL has no `json` column type — not parsed here.</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Strictly increasing publication order the relay polls by — `bigint IDENTITY(1,1)`, never assigned by the application.</summary>
    public long Seq { get; set; }

    public string? TraceParent { get; set; }
}
