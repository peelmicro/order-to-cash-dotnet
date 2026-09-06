using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Infrastructure.Outbox;
using OrderToCash.SharedKernel;
using RowInvoice = OrderToCash.Billing.Infrastructure.Persistence.Entities.Invoice;

namespace OrderToCash.Billing.Infrastructure.Persistence;

/// <summary>
/// design.md §6.2. <see cref="FindByOrderReferenceAsync"/> is the fast path
/// the sweeper hits on every retry — `AsNoTracking`, no hint, no
/// transaction. <see cref="LockByOrderReferenceAsync"/> is the `B7`
/// authority — inside the ambient transaction, AFTER the credits lock
/// (`BI8`), under `WITH (UPDLOCK, HOLDLOCK, ROWLOCK)`. <see cref="SaveAsync"/>
/// never UPDATEs, never DELETEs, never MERGEs — every invoice and every line
/// is brand new.
/// </summary>
public sealed class EfCoreInvoiceRepository(BillingDbContext db, OutboxWriter outboxWriter, IClock clock) : IInvoiceRepository
{
    /// <summary>The lock statement's literal column list, in the order <c>InvoiceConfiguration</c> declares them — every mapped column of <see cref="RowInvoice"/>, because <c>FromSqlInterpolated</c> requires ALL of them in the projection.</summary>
    public static readonly IReadOnlyList<string> InvoiceLockColumnNames =
        ["id", "invoice_reference", "invoice_date", "company_code", "retailer_code", "order_reference", "amount", "discount", "total_amount", "currency_code", "status", "paid_at", "created_at", "updated_at"];

    public async Task<InvoiceSnapshot?> FindByOrderReferenceAsync(OrderNumber orderReference, CancellationToken cancellationToken)
    {
        var row = await db.Invoices
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.OrderReference == orderReference.Value, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var items = await db.InvoiceItems
            .AsNoTracking()
            .Where(i => i.InvoiceId == row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return InvoiceRowMapper.ToSnapshot(row, items);
    }

    /// <summary>
    /// The `B7` authority — NEVER `db.Invoices.Where(...).FirstOrDefaultAsync()`
    /// on this path: LINQ composition drops the hint and hands back an
    /// unlocked versioned read that looks identical in code review (ledger
    /// `L7`'s established hazard).
    /// </summary>
    public async Task<InvoiceSnapshot?> LockByOrderReferenceAsync(OrderNumber orderReference, CancellationToken cancellationToken)
    {
        var row = await db.Invoices
            .FromSqlInterpolated(
                $@"SELECT id, invoice_reference, invoice_date, company_code, retailer_code, order_reference, amount, discount, total_amount, currency_code, status, paid_at, created_at, updated_at
                   FROM   dbo.invoices WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                   WHERE  order_reference = {orderReference.Value}")
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        var items = await db.InvoiceItems
            .AsNoTracking()
            .Where(i => i.InvoiceId == row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return InvoiceRowMapper.ToSnapshot(row, items);
    }

    /// <summary>
    /// Inserts the invoice header + its lines, then drains the aggregate's
    /// ONE `invoice.issued.v1` into the outbox — one AWAITED statement at a
    /// time, copied verbatim (reasoning and shape) from
    /// <c>EfCoreBuyerCreditRepository.InsertOutboxRowAsync</c> (ledger
    /// `L16`) — then calls `SaveChangesAsync`, clearing domain events only
    /// after everything above returned (`OI9`). No `UPDATE`, no `DELETE`,
    /// no `MERGE`.
    /// </summary>
    public async Task SaveAsync(Invoice invoice, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;

        db.Invoices.Add(InvoiceRowMapper.ToNewRow(invoice, now));

        foreach (var line in invoice.Lines)
        {
            db.InvoiceItems.Add(InvoiceRowMapper.ToNewRow(line.ToSnapshot(), invoice.Id.Value, now));
        }

        foreach (var outboxRow in outboxWriter.BuildRows(invoice.DomainEvents))
        {
            await InsertOutboxRowAsync(outboxRow, cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        invoice.ClearDomainEvents();
    }

    /// <summary>Copied verbatim (reasoning and shape) from <c>EfCoreBuyerCreditRepository.InsertOutboxRowAsync</c> (ledger `L16`) — never `AddRange`.</summary>
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
