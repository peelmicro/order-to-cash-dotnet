// COPY OF — src/Orders/Infrastructure/Outbox/OutboxWriter.cs
using System.Text.Json;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Wire;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Domain.Events;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;
using OrderToCash.SharedKernel;

namespace OrderToCash.Fulfillment.Infrastructure.Outbox;

/// <summary>
/// <see cref="IReadOnlyList{IDomainEvent}"/> -&gt; <see cref="OutboxMessage"/>
/// rows. Called from inside the write-model transaction
/// (<c>EfCoreStockItemRepository.SaveChangesAsync</c>), never by the relay,
/// which only ever reads what this class already wrote (`R14`'s "only the
/// relay publishes").
/// </summary>
public sealed class OutboxWriter(IClock clock)
{
    /// <summary>
    /// Builds one row per event, in list (= raise) order, so <c>seq</c>
    /// reflects emission order once the rows are inserted. For each event:
    /// <see cref="DomainEventEnvelope.Validate"/> runs first (`R11`'s refusal
    /// clause); then the event type must be a member of
    /// <see cref="FactCatalog.PayloadTypesByEventType"/>'s keys; only then is
    /// the row built. Assigns no <c>seq</c>, leaves <c>published_at</c> and
    /// <c>trace_parent</c> null, and takes <c>created_at</c> from
    /// <see cref="IClock"/> so tests control time.
    /// </summary>
    public IReadOnlyList<OutboxMessage> BuildRows(IReadOnlyList<IDomainEvent> domainEvents)
    {
        var rows = new List<OutboxMessage>(domainEvents.Count);
        var createdAt = clock.UtcNow.UtcDateTime;

        foreach (var domainEvent in domainEvents)
        {
            var stockEvent = (StockDomainEvent)domainEvent;

            DomainEventEnvelope.Validate(stockEvent);

            if (!FactCatalog.PayloadTypesByEventType.ContainsKey(stockEvent.EventType))
            {
                throw new InvalidOperationException(
                    $"Outbox writer refuses to store a fact whose eventType '{stockEvent.EventType}' is not in the declared FactCatalog.");
            }

            var payload = StockFactPayloadMapper.ToPayload(stockEvent);

            rows.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventId = stockEvent.EventId.Value,
                EventType = stockEvent.EventType,
                AggregateId = stockEvent.AggregateId.Value,
                CorrelationId = stockEvent.CorrelationId.Value,
                CausationId = stockEvent.CausationId.Value,
                Payload = JsonSerializer.Serialize(payload, JsonWire.Options),
                OccurredAt = stockEvent.OccurredAt.UtcDateTime,
                PublishedAt = null,
                CreatedAt = createdAt,
                TraceParent = null,
            });
        }

        return rows;
    }
}
