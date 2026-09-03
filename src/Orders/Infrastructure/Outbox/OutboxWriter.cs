using System.Text.Json;
using OrderToCash.Contracts.Facts;
using OrderToCash.Contracts.Wire;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Domain.Events;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Infrastructure.Outbox;

/// <summary>
/// <see cref="IReadOnlyList{IDomainEvent}"/> -&gt; <see cref="OutboxMessage"/>
/// rows, column by column exactly as design.md §4.4's table prescribes.
/// Called from inside the write-model transaction
/// (<see cref="Persistence.EfCoreOrderRepository.SaveChangesAsync"/>), never
/// by the relay, which only ever reads what this class already wrote (R14's
/// "only the relay publishes" — this class is the OTHER half: only the
/// writer stores).
/// </summary>
public sealed class OutboxWriter(IClock clock)
{
    /// <summary>
    /// Builds one row per event, in list (= raise) order, so <c>seq</c>
    /// reflects emission order once the rows are inserted. For each event:
    /// <see cref="DomainEventEnvelope.Validate"/> runs first (R11's refusal
    /// clause — an incomplete envelope never reaches storage); then the
    /// event type must be a member of
    /// <see cref="FactCatalog.PayloadTypesByEventType"/>'s keys (a
    /// catalogued fact with a mapped payload type); only then is the row
    /// built. Assigns no <c>seq</c>, leaves <c>published_at</c> and
    /// <c>trace_parent</c> null, and takes <c>created_at</c> from
    /// <see cref="IClock"/> so tests control time.
    /// </summary>
    public IReadOnlyList<OutboxMessage> BuildRows(IReadOnlyList<IDomainEvent> domainEvents)
    {
        var rows = new List<OutboxMessage>(domainEvents.Count);
        var createdAt = clock.UtcNow.UtcDateTime;

        foreach (var domainEvent in domainEvents)
        {
            var orderEvent = (OrderDomainEvent)domainEvent;

            DomainEventEnvelope.Validate(orderEvent);

            if (!FactCatalog.PayloadTypesByEventType.ContainsKey(orderEvent.EventType))
            {
                throw new InvalidOperationException(
                    $"Outbox writer refuses to store a fact whose eventType '{orderEvent.EventType}' is not in the declared FactCatalog.");
            }

            var payload = OrderFactPayloadMapper.ToPayload(orderEvent);

            rows.Add(new OutboxMessage
            {
                Id = Guid.NewGuid(),
                EventId = orderEvent.EventId.Value,
                EventType = orderEvent.EventType,
                AggregateId = orderEvent.AggregateId.Value,
                CorrelationId = orderEvent.CorrelationId.Value,
                CausationId = orderEvent.CausationId.Value,
                Payload = JsonSerializer.Serialize(payload, JsonWire.Options),
                OccurredAt = orderEvent.OccurredAt.UtcDateTime,
                PublishedAt = null,
                CreatedAt = createdAt,
                TraceParent = null,
            });
        }

        return rows;
    }
}
