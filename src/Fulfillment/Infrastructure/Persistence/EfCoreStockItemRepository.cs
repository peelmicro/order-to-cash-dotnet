using Microsoft.EntityFrameworkCore;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Domain;
using OrderToCash.Fulfillment.Infrastructure.Outbox;
using OrderToCash.SharedKernel;
using RowReservation = OrderToCash.Fulfillment.Infrastructure.Persistence.Entities.Reservation;
using RowStock = OrderToCash.Fulfillment.Infrastructure.Persistence.Entities.Stock;

namespace OrderToCash.Fulfillment.Infrastructure.Persistence;

/// <summary>
/// The lock protocol (design.md §4.3, §4.4) + save + outbox drain (§7.2).
/// Keeps, per stock row loaded or added through THIS instance, the tracked
/// row (and its reservation rows) it maps to — an identity map scoped to one
/// unit of work, the shape <c>EfCoreOrderRepository</c> already uses.
/// </summary>
public sealed class EfCoreStockItemRepository(FulfillmentDbContext db, OutboxWriter outboxWriter, IClock clock) : IStockItemRepository
{
    /// <summary>
    /// The stock-lock statement's literal column list, in the order
    /// <c>StockConfiguration</c> declares them — every mapped column of
    /// <see cref="RowStock"/>, because <c>FromSqlInterpolated</c> requires
    /// ALL of them in the projection. Exposed so <c>StockClaimProjectionTests</c>
    /// (the <c>OutboxClaimProjectionTests</c> instrument) can compare this
    /// list against the <c>IEntityType</c>'s mapped properties mechanically.
    /// </summary>
    public static readonly IReadOnlyList<string> StockClaimColumnNames =
        ["id", "company_code", "product_code", "units", "reserved_units", "low_stock_threshold", "created_at", "updated_at"];

    /// <summary>The reservations-lock statement's literal column list — every mapped column of <see cref="RowReservation"/>.</summary>
    public static readonly IReadOnlyList<string> ReservationClaimColumnNames =
        ["id", "stock_id", "company_code", "retailer_code", "product_code", "order_reference", "units", "status", "created_at", "updated_at"];

    private readonly Dictionary<Guid, (StockItem Aggregate, RowStock Row, List<RowReservation> ReservationRows)> _tracked = [];

    public async Task<StockLockResult> LockForOrderAsync(string companyCode, IReadOnlyList<string> productCodes, OrderNumber orderReference, CancellationToken cancellationToken)
    {
        var orderedCodes = StockLockOrder.Fix(productCodes);

        // Step 1 — ONE single-row locking statement PER distinct product
        // code, issued in the FS19 order: MS-SQL gives no guarantee about a
        // multi-row seek's lock-acquisition order, so the total order must
        // be fixed by the application rather than by an ORDER BY inside one
        // multi-row statement (design.md §4.3).
        var stockRows = new List<RowStock>();
        foreach (var productCode in orderedCodes)
        {
            var row = await db.Stocks
                .FromSqlInterpolated(
                    $@"SELECT id, company_code, product_code, units, reserved_units, low_stock_threshold, created_at, updated_at
                       FROM   dbo.stock WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                       WHERE  company_code = {companyCode} AND product_code = {productCode}")
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (row is not null)
            {
                stockRows.Add(row);
            }
        }

        // Step 2 — the order's EXISTING reservations, a LOCKING read, AFTER
        // the stock locks (design.md §4.3's "why the reservations read comes
        // second"). No product filter: FS5's short-circuit must see a
        // terminal reservation on a product this retry omitted, too.
        var reservationRows = await db.Reservations
            .FromSqlInterpolated(
                $@"SELECT id, stock_id, company_code, retailer_code, product_code, order_reference, units, status, created_at, updated_at
                   FROM   dbo.reservations WITH (UPDLOCK, HOLDLOCK)
                   WHERE  order_reference = {orderReference.Value}")
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var itemsByProductCode = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var stockRow in stockRows)
        {
            var itemReservationRows = reservationRows.Where(r => r.StockId == stockRow.Id).ToList();
            var aggregate = StockRowMapper.ToDomain(stockRow, itemReservationRows);
            _tracked[stockRow.Id] = (aggregate, stockRow, itemReservationRows);
            itemsByProductCode[stockRow.ProductCode] = aggregate;
        }

        var existing = reservationRows
            .Select(r => new ReservationSnapshot(UniqueId.From(r.Id), OrderNumber.Parse(r.OrderReference), r.CompanyCode, r.RetailerCode, r.ProductCode, r.Units, ReservationStatuses.Parse(r.Status)))
            .ToList();

        return new StockLockResult(itemsByProductCode, existing);
    }

    public async Task<IReadOnlyDictionary<string, StockItem>> LockItemsAsync(string companyCode, IReadOnlyList<string> productCodes, CancellationToken cancellationToken)
    {
        var orderedCodes = StockLockOrder.Fix(productCodes);

        var items = new Dictionary<string, StockItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var productCode in orderedCodes)
        {
            var row = await db.Stocks
                .FromSqlInterpolated(
                    $@"SELECT id, company_code, product_code, units, reserved_units, low_stock_threshold, created_at, updated_at
                       FROM   dbo.stock WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                       WHERE  company_code = {companyCode} AND product_code = {productCode}")
                .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

            if (row is null)
            {
                continue;
            }

            var aggregate = StockRowMapper.ToDomain(row, []);
            _tracked[row.Id] = (aggregate, row, []);
            items[row.ProductCode] = aggregate;
        }

        return items;
    }

    public async Task<OrderReservationLookup?> ProductCodesOfOrderAsync(OrderNumber orderReference, CancellationToken cancellationToken)
    {
        // Non-locking pre-read (design.md §4.4 step 0) — decides ONLY
        // whether to open a transaction. No hint, no lock: the authoritative
        // decision is re-made under lock inside the transaction.
        var rows = await db.Reservations
            .AsNoTracking()
            .Where(r => r.OrderReference == orderReference.Value)
            .Select(r => new { r.CompanyCode, r.ProductCode })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return null;
        }

        return new OrderReservationLookup(
            rows[0].CompanyCode,
            [.. rows.Select(r => r.ProductCode).Distinct(StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>
    /// Syncs each loaded aggregate's mutable fields onto its tracked row,
    /// adds/updates reservation rows, drains EVERY loaded aggregate's
    /// <c>DomainEvents</c> into outbox rows — inserted ONE AWAITED statement
    /// at a time (copied verbatim reasoning from <c>EfCoreOrderRepository.InsertOutboxRowAsync</c>
    /// — EF Core's SQL Server provider does not preserve <c>Add</c> order
    /// when assigning IDENTITY values, and <c>seq</c> is the entire
    /// publication-order guarantee) — then calls <c>SaveChangesAsync</c>,
    /// clearing domain events only after everything above returned (`OI9`).
    /// No upsert is rendered anywhere: every row here was loaded under a
    /// lock in this same transaction, so an <c>UPDATE</c> by primary key is
    /// exactly right (ledger L5).
    /// </summary>
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;

        foreach (var (aggregate, row, reservationRows) in _tracked.Values)
        {
            StockRowMapper.SyncMutableFields(row, aggregate, now);
            SyncReservations(aggregate, row, reservationRows, now);

            foreach (var outboxRow in outboxWriter.BuildRows(aggregate.DomainEvents))
            {
                await InsertOutboxRowAsync(outboxRow, cancellationToken).ConfigureAwait(false);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var (aggregate, _, _) in _tracked.Values)
        {
            aggregate.ClearDomainEvents();
        }
    }

    private void SyncReservations(StockItem aggregate, RowStock row, List<RowReservation> reservationRows, DateTime now)
    {
        var existingById = reservationRows.ToDictionary(r => r.Id);

        foreach (var view in aggregate.Reservations)
        {
            if (existingById.TryGetValue(view.Id.Value, out var existingRow))
            {
                existingRow.Status = ReservationStatuses.ToToken(view.Status);
                existingRow.UpdatedAt = now;
            }
            else
            {
                var newRow = new RowReservation
                {
                    Id = view.Id.Value,
                    StockId = row.Id,
                    CompanyCode = view.CompanyCode,
                    RetailerCode = view.RetailerCode,
                    ProductCode = view.ProductCode,
                    OrderReference = view.OrderReference.Value,
                    Units = view.Units,
                    Status = ReservationStatuses.ToToken(view.Status),
                    CreatedAt = now,
                    UpdatedAt = now,
                };

                db.Reservations.Add(newRow);
                reservationRows.Add(newRow);
            }
        }
    }

    /// <summary>Copied verbatim (reasoning and shape) from <c>EfCoreOrderRepository.InsertOutboxRowAsync</c> (ledger L8) — never <c>AddRange</c>.</summary>
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
