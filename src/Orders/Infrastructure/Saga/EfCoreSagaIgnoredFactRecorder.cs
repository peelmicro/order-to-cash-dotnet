using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Saga;

/// <summary>
/// The <c>saga_ignored_facts</c> adapter (design.md §5.4) — inserts through
/// the AMBIENT scoped <see cref="OrdersDbContext"/>, exactly as
/// <see cref="Persistence.EfCoreOrderNumberAllocator"/> already does: no
/// <c>tx</c> parameter, the caller's <c>IUnitOfWork</c> transaction is what
/// makes the write durable together with the dedup record (R25, SO8). No new
/// table, no new column — <c>SagaIgnoredFact</c> and its configuration
/// already exist.
/// </summary>
public sealed class EfCoreSagaIgnoredFactRecorder(OrdersDbContext db, IClock clock) : ISagaIgnoredFactRecorder
{
    public async Task RecordAsync(SagaIgnoredFactRecord record, CancellationToken cancellationToken)
    {
        var row = new SagaIgnoredFact
        {
            Id = Guid.NewGuid(),
            EventId = record.EventId,
            EventType = record.EventType,
            OrderId = record.OrderId,
            CorrelationId = record.CorrelationId,
            ObservedStatus = record.ObservedStatus is { } observed ? OrderStatuses.ToToken(observed) : null,
            ExpectedStatus = record.ExpectedStatus is { } expected ? OrderStatuses.ToToken(expected) : null,
            Marker = SagaIgnoredFactMarkers.ToToken(record.Marker),
            RecordedAt = clock.UtcNow.UtcDateTime,
        };

        db.SagaIgnoredFacts.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
