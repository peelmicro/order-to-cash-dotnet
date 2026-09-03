using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Domain;
using OrderToCash.Orders.Infrastructure.Outbox;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.SharedKernel;
using RowOrder = OrderToCash.Orders.Infrastructure.Persistence.Entities.Order;

namespace OrderToCash.Orders.Infrastructure.Persistence;

/// <summary>
/// The four <see cref="IOrderRepository"/> methods, over the scoped
/// <see cref="OrdersDbContext"/> (design.md §4.2 – §4.5). Bounded exactly as
/// design.md §4.3 fixes: the row &lt;-&gt; aggregate mapping of
/// specs/orders_aggregate/design.md §8, and nothing else — no order-number
/// allocation, no NATS, no command (feature 15's job).
/// </summary>
/// <remarks>
/// Keeps, per aggregate loaded or added through THIS instance, the tracked
/// row it maps to — an identity map scoped to one unit of work (design.md
/// §4.3's "insert-or-update semantics": the repository already knows
/// whether an aggregate is new or reloaded, rather than probing the
/// database). <see cref="SaveChangesAsync"/> only ever acts on aggregates
/// registered this way.
/// </remarks>
public sealed class EfCoreOrderRepository(OrdersDbContext db, OutboxWriter outboxWriter) : IOrderRepository
{
    private readonly Dictionary<UniqueId, (Domain.Order Aggregate, RowOrder Row)> _tracked = [];

    public async Task AddAsync(Domain.Order order, CancellationToken cancellationToken)
    {
        var row = await OrderRowMapper.ToNewRowAsync(db, order, cancellationToken);
        db.Orders.Add(row);
        _tracked[order.Id] = (order, row);
    }

    public async Task<Domain.Order?> GetByIdAsync(UniqueId id, CancellationToken cancellationToken)
    {
        var row = await db.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id.Value, cancellationToken);
        return row is null ? null : await TrackAndMapAsync(row, cancellationToken);
    }

    public async Task<Domain.Order?> GetByReferenceAsync(OrderNumber reference, CancellationToken cancellationToken)
    {
        var row = await db.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.OrderReference == reference.Value, cancellationToken);
        return row is null ? null : await TrackAndMapAsync(row, cancellationToken);
    }

    /// <summary>
    /// Drains every registered aggregate's <c>DomainEvents</c> into
    /// <c>outbox</c> rows (inserted one at a time, sequentially — see
    /// <see cref="InsertOutboxRowAsync"/> for why), syncs every registered
    /// aggregate's mutable fields onto its tracked row, calls
    /// <c>DbContext.SaveChangesAsync</c> for the aggregate's own rows, and
    /// calls <c>ClearDomainEvents()</c> only after everything above has
    /// returned — specs/orders_aggregate/design.md §7.5 point 3, followed
    /// literally (design.md §4.5's OI9 hazard: clearing before the save
    /// would lose the events on a rollback, and a retry on the same
    /// instance would then commit aggregate rows with no outbox rows). All
    /// of it runs inside the SAME ambient transaction <see cref="IUnitOfWork"/>
    /// opened, so R13's atomicity holds regardless of how many statements
    /// this method issues.
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var (aggregate, row) in _tracked.Values)
        {
            await OrderRowMapper.SyncMutableFieldsAsync(db, aggregate, row, cancellationToken);

            foreach (var outboxRow in outboxWriter.BuildRows(aggregate.DomainEvents))
            {
                await InsertOutboxRowAsync(outboxRow, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var (aggregate, _) in _tracked.Values)
        {
            aggregate.ClearDomainEvents();
        }
    }

    /// <summary>
    /// Inserts ONE <c>outbox</c> row via a raw, parameterised, awaited
    /// <c>INSERT</c> — deliberately NOT <c>db.OutboxMessages.Add(...)</c>
    /// batched through the change tracker. Measured directly during this
    /// feature (progress/impl_outbox_and_idempotency.md): when two or more
    /// rows with a CLIENT-generated key (<c>Id</c>, a <c>uniqueidentifier</c>
    /// this application sets) and a DATABASE-generated key
    /// (<c>Seq</c>, <c>IDENTITY(1,1)</c>) are added via
    /// <c>AddRange</c>/multiple <c>Add</c> calls inside ONE
    /// <c>SaveChangesAsync()</c>, EF Core's SQL Server provider does not
    /// preserve Add-call order when assigning or returning the IDENTITY
    /// values — observed with <c>MaxBatchSize(1)</c> forced too, so it is
    /// not a batching artefact. <c>seq</c> is this feature's entire
    /// publication-order guarantee (R12, OI2), so it cannot be left to a
    /// mechanism that does not preserve order. A separate, awaited
    /// round trip per row — still inside the ambient transaction
    /// <see cref="IUnitOfWork"/> opened — makes SQL Server's own IDENTITY
    /// counter (which DOES increment in statement-execution order) the only
    /// thing <c>seq</c> depends on.
    /// </summary>
    private async Task InsertOutboxRowAsync(OutboxMessage row, CancellationToken cancellationToken) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO dbo.outbox
                 (id, event_id, event_type, aggregate_id, correlation_id, causation_id, payload, occurred_at, published_at, created_at, trace_parent)
             VALUES
                 ({row.Id}, {row.EventId}, {row.EventType}, {row.AggregateId}, {row.CorrelationId}, {row.CausationId}, {row.Payload}, {row.OccurredAt}, {row.PublishedAt}, {row.CreatedAt}, {row.TraceParent})
             """,
            cancellationToken);

    private async Task<Domain.Order> TrackAndMapAsync(RowOrder row, CancellationToken cancellationToken)
    {
        var aggregate = await OrderRowMapper.ToDomainAsync(db, row, cancellationToken);
        _tracked[aggregate.Id] = (aggregate, row);
        return aggregate;
    }
}
