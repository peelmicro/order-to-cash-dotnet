using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Infrastructure.Outbox;
using OrderToCash.SharedKernel;
using RowDespatch = OrderToCash.Fulfillment.Infrastructure.Persistence.Entities.Despatch;
using RowDespatchItem = OrderToCash.Fulfillment.Infrastructure.Persistence.Entities.DespatchItem;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence;

/// <summary>
/// The despatch-side repository — plain SELECT for the F8 read, plain INSERT
/// (never upsert — a despatch is created once, ledger L5) for the write,
/// draining the outbox via the SAME <see cref="OutboxWriter"/>
/// <see cref="EfCoreStockItemRepository"/> uses.
/// </summary>
public sealed class EfCoreDespatchRepository(FulfillmentDbContext db, OutboxWriter outboxWriter, IClock clock) : IDespatchRepository
{
    public async Task<DespatchSnapshot?> FindByOrderReferenceAsync(OrderNumber orderReference, CancellationToken cancellationToken)
    {
        var row = await db.Despatches
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.OrderReference == orderReference.Value, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var items = await db.DespatchItems
            .AsNoTracking()
            .Where(i => i.DespatchId == row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new DespatchSnapshot(
            UniqueId.From(row.Id),
            row.DespatchReference,
            new DateTimeOffset(DateTime.SpecifyKind(row.DespatchDate, DateTimeKind.Utc)),
            OrderNumber.Parse(row.OrderReference),
            row.CompanyCode,
            row.RetailerCode,
            [.. items.Select(i => new DespatchLineEntry(i.ProductCode, new Quantity(i.Units)))]);
    }

    /// <summary>
    /// Inserts the despatch header + its lines, then drains the aggregate's
    /// ONE <c>order.despatched.v1</c> into the outbox — ONE awaited statement
    /// at a time, copied verbatim (reasoning and shape) from
    /// <c>EfCoreStockItemRepository.InsertOutboxRowAsync</c> (ledger L8: EF
    /// Core's SQL Server provider does not preserve <c>Add</c> order when
    /// assigning IDENTITY values, and <c>seq</c> is the entire
    /// publication-order guarantee) — then calls <c>SaveChangesAsync</c>,
    /// clearing domain events only after everything above returned (`OI9`).
    /// </summary>
    public async Task SaveAsync(DespatchAdvice despatch, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;

        db.Despatches.Add(new RowDespatch
        {
            Id = despatch.Id.Value,
            DespatchReference = despatch.DespatchReference,
            DespatchDate = despatch.DespatchDate.UtcDateTime,
            CompanyCode = despatch.CompanyCode,
            RetailerCode = despatch.RetailerCode,
            OrderReference = despatch.OrderReference.Value,
            CreatedAt = now,
            UpdatedAt = now,
        });

        foreach (var line in despatch.Lines)
        {
            db.DespatchItems.Add(new RowDespatchItem
            {
                Id = Guid.NewGuid(),
                DespatchId = despatch.Id.Value,
                ProductCode = line.ProductCode,
                Units = line.Units.Value,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        foreach (var outboxRow in outboxWriter.BuildRows(despatch.DomainEvents))
        {
            await InsertOutboxRowAsync(outboxRow, cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        despatch.ClearDomainEvents();
    }

    /// <summary>Copied verbatim (reasoning and shape) from <c>EfCoreStockItemRepository.InsertOutboxRowAsync</c> (ledger L8) — never <c>AddRange</c>.</summary>
    private async Task InsertOutboxRowAsync(Persistence.Entities.OutboxMessage row, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO dbo.outbox
                 (id, event_id, event_type, aggregate_id, correlation_id, causation_id, payload, occurred_at, published_at, created_at, trace_parent)
             VALUES
                 ({row.Id}, {row.EventId}, {row.EventType}, {row.AggregateId}, {row.CorrelationId}, {row.CausationId}, {row.Payload}, {row.OccurredAt}, {row.PublishedAt}, {row.CreatedAt}, {row.TraceParent})
             """,
            cancellationToken);
}
