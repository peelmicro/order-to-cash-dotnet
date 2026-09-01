namespace OrderToCash.Orders.Infrastructure.Persistence.Entities;

/// <summary>
/// Persistence row for `otc_orders.saga_ignored_facts` — the durable record
/// of facts the saga deliberately ignored (Databases doc §4.4), written in
/// the same transaction as the dedup record, so "why did the saga ignore
/// this?" is answered by the database, not a log line.
/// </summary>
public sealed class SagaIgnoredFact
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public string EventType { get; set; } = string.Empty;

    public Guid? OrderId { get; set; }

    public Guid CorrelationId { get; set; }

    public string? ObservedStatus { get; set; }

    public string? ExpectedStatus { get; set; }

    public string Marker { get; set; } = string.Empty;

    public DateTime RecordedAt { get; set; }
}
