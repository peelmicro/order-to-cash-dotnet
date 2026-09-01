namespace OrderToCash.Orders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_orders.outbox` — the transactional outbox
/// (Databases doc §4.3). Every domain fact is inserted here in the same
/// transaction as the aggregate change that produced it; a relay process
/// polls unpublished rows (`PublishedAt IS NULL`, ordered by <see
/// cref="Seq"/>), publishes them to Kafka and stamps <see
/// cref="PublishedAt"/> only after the broker acknowledges.
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
