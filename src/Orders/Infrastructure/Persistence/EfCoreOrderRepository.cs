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

    public async Task AddAsync(Domain.Order order, Guid? requestId, CancellationToken cancellationToken)
    {
        var row = await OrderRowMapper.ToNewRowAsync(db, order, cancellationToken);
        row.RequestId = requestId;
        db.Orders.Add(row);
        _tracked[order.Id] = (order, row);
    }

    /// <summary>
    /// Feature <c>observability_reliability</c>, ledger L4. Clears the
    /// scoped <see cref="OrdersDbContext"/>'s <c>ChangeTracker</c> FIRST —
    /// on the RI2 fast path (the very first thing the handler does) this is
    /// a no-op, but on the RI3 re-read (called from inside the handler's
    /// catch, after <c>IUnitOfWork.ExecuteAsync</c> has rolled back) it
    /// detaches the losing attempt's still-tracked row so nothing in this
    /// scope can accidentally re-write it — and it is why this read never
    /// needs a second, EF-specific port on <see cref="IUnitOfWork"/>, which
    /// design.md §1.1 does not list among Half B's touched files. The query
    /// itself is <c>AsNoTracking</c>, so it never re-enters <see cref="_tracked"/>
    /// either.
    /// </summary>
    public async Task<Domain.Order?> FindByRequestIdAsync(Guid requestId, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();

        var row = await db.Orders.AsNoTracking().Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.RequestId == requestId, cancellationToken);

        return row is null ? null : await OrderRowMapper.ToDomainAsync(db, row, cancellationToken);
    }

    /// <summary>
    /// Feature <c>operator_cancel_races_saga_forward_progress</c> (id 62) —
    /// takes <see cref="LockOrderRowAsync"/>'s <c>UPDLOCK, ROWLOCK</c> point
    /// read FIRST, on EVERY call. Every caller of this method in this
    /// codebase (<c>CancelOrderCommandHandler</c>, <c>SagaFactHandler</c>,
    /// <c>SagaFirstParkDeadLetterHandler</c>) loads the order to MUTATE it
    /// inside the SAME ambient transaction — never a pure display read, that
    /// is the Mongo read model's job — so locking unconditionally here is
    /// never a wider lock than the existing call sites already imply.
    /// </summary>
    public async Task<Domain.Order?> GetByIdAsync(UniqueId id, CancellationToken cancellationToken)
    {
        await LockOrderRowAsync(id.Value, cancellationToken).ConfigureAwait(false);

        var row = await db.Orders.Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == id.Value, cancellationToken);
        return row is null ? null : await TrackAndMapAsync(row, cancellationToken);
    }

    /// <summary>
    /// Id 62's chosen defence: an explicit <c>UPDLOCK, ROWLOCK</c> point
    /// read on <c>dbo.orders</c>, held until the CALLER's ambient
    /// transaction (<c>IUnitOfWork.ExecuteAsync</c>) commits or rolls back.
    /// Under this database's <c>READ_COMMITTED_SNAPSHOT ON</c>, a PLAIN
    /// <c>SELECT</c> reads a row-versioned snapshot and never blocks on
    /// another writer's held locks — precisely the gap
    /// <c>CancelOrderCommandHandler</c>'s own remarks name: two independent
    /// transactions could each read a CONSISTENT-BUT-DIFFERENT-FROM-THE-OTHER
    /// snapshot of the SAME order row and both proceed, unaware of each
    /// other. A locking hint always takes precedence over RCSI's
    /// row-versioning for THAT statement (it is not overridden by the
    /// ambient <c>ReadCommitted</c> isolation level <see cref="EfCoreUnitOfWork"/>
    /// opens), so a concurrent caller requesting the SAME row's
    /// <c>UPDLOCK</c>/<c>X</c> lock WAITS rather than reading ahead, and
    /// reads the FRESH, post-commit row once the lock is granted — the
    /// second writer waits, it does not deadlock (both callers here only
    /// ever acquire ONE lock, on this ONE row, so no cross-resource cycle
    /// can form). <c>ROWLOCK</c> keeps the cost scoped to THIS order — no
    /// other order's throughput is affected. Deliberately no
    /// <c>READPAST</c> here, unlike <c>EfCoreSagaCommandStore.ClaimDueAsync</c>'s
    /// own <c>UPDLOCK</c> (measured there to skip rather than block): a
    /// losing writer on the SAME order must wait for the truth, never
    /// silently skip its own order's row.
    /// </summary>
    private async Task LockOrderRowAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Database
            .SqlQuery<int>($"SELECT TOP (1) 1 AS Value FROM dbo.orders WITH (UPDLOCK, ROWLOCK) WHERE id = {id}")
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

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
