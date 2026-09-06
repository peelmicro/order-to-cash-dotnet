using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application;
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

    /// <summary>The invoice row this repository INSTANCE last locked via <see cref="LockByIdAsync"/> — the SAME identity-map-scoped-to-one-unit-of-work shape <c>EfCoreBuyerCreditRepository</c> already uses for <c>_currentRow</c>. <see cref="MarkPaidAsync"/> refuses to run against any other instance.</summary>
    private RowInvoice? _currentLockedRow;

    // MS-SQL error 2601 ("cannot insert duplicate key row in object with
    // unique index") and 2627 (its constraint-shaped sibling) — the SAME
    // pair `ProcessedEventLedger`/`EfCoreSagaCommandStore` already
    // recognise. Here they mean exactly one thing: two requests racing the
    // SAME `paymentReference` against two DIFFERENT invoices both passed
    // the fast path (`R48`'s belt-and-braces backstop, N11's shape).
    private const int DuplicateKeyRow = 2601;
    private const int DuplicateKeyConstraint = 2627;

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

    // -- feature 22 (billing_remittance_intake) additions below --------

    public async Task<InvoiceSnapshot?> FindByIdAsync(UniqueId invoiceId, CancellationToken cancellationToken)
    {
        var row = await db.Invoices
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == invoiceId.Value, cancellationToken)
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

    public async Task<InvoiceSnapshot?> FindByInvoiceReferenceAsync(string invoiceReference, CancellationToken cancellationToken)
    {
        var row = await db.Invoices
            .AsNoTracking()
            .SingleOrDefaultAsync(i => i.InvoiceReference == invoiceReference, cancellationToken)
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

    public async Task<PaymentSnapshot?> FindPaymentByReferenceAsync(string paymentReference, CancellationToken cancellationToken)
    {
        var row = await db.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.PaymentReference == paymentReference, cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : PaymentRowMapper.ToSnapshot(row);
    }

    /// <summary>
    /// A PLAIN (un-hinted) read, deliberately — issued only AFTER
    /// <see cref="LockByIdAsync"/>'s lock on this same invoice row is
    /// granted, so under `EfCoreUnitOfWork`'s `ReadCommitted` isolation
    /// (RCSI) it observes the latest committed `payments` row rather than a
    /// transaction-scoped snapshot taken before the lock wait (design.md
    /// §7.3's ledger note — the property backlog id 54 asks every such
    /// un-hinted in-transaction re-read to name explicitly).
    /// </summary>
    public async Task<PaymentSnapshot?> FindPaymentByInvoiceIdAsync(UniqueId invoiceId, CancellationToken cancellationToken)
    {
        var row = await db.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.InvoiceId == invoiceId.Value, cancellationToken)
            .ConfigureAwait(false);

        return row is null ? null : PaymentRowMapper.ToSnapshot(row);
    }

    /// <summary>
    /// The `B8` authority — NEVER `db.Invoices.Where(...).FirstOrDefaultAsync()`
    /// on this path, for the same reason <see cref="LockByOrderReferenceAsync"/>
    /// never does (ledger `L7`). Keeps the tracked row in <see cref="_currentLockedRow"/>
    /// so <see cref="MarkPaidAsync"/> can UPDATE it without a second round trip.
    /// </summary>
    public async Task<InvoiceSnapshot?> LockByIdAsync(UniqueId invoiceId, CancellationToken cancellationToken)
    {
        var row = await db.Invoices
            .FromSqlInterpolated(
                $@"SELECT id, invoice_reference, invoice_date, company_code, retailer_code, order_reference, amount, discount, total_amount, currency_code, status, paid_at, created_at, updated_at
                   FROM   dbo.invoices WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                   WHERE  id = {invoiceId.Value}")
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        _currentLockedRow = row;

        var items = await db.InvoiceItems
            .AsNoTracking()
            .Where(i => i.InvoiceId == row.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return InvoiceRowMapper.ToSnapshot(row, items);
    }

    /// <summary>
    /// The ONE UPDATE this repository ever issues (design.md §6.2's own
    /// anticipation) — <paramref name="invoice"/>'s <c>status</c>/<c>paidAt</c>
    /// mirrored onto the row <see cref="LockByIdAsync"/> loaded and is
    /// still tracking, then the `payments` INSERT, then the outbox drain
    /// (exactly one `payment.received.v1`, `R47`) — one awaited `INSERT` at
    /// a time, the SAME discipline <see cref="SaveAsync"/> already uses —
    /// then `SaveChangesAsync`, clearing domain events only after
    /// everything above returned (`OI9`).
    /// </summary>
    public async Task MarkPaidAsync(Invoice invoice, MarkPaidInput payment, CancellationToken cancellationToken)
    {
        if (_currentLockedRow is null || _currentLockedRow.Id != invoice.Id.Value)
        {
            throw new InvalidOperationException("MarkPaidAsync was called with an invoice this repository did not load through LockByIdAsync.");
        }

        var now = clock.UtcNow.UtcDateTime;

        _currentLockedRow.Status = invoice.Status;
        _currentLockedRow.PaidAt = invoice.PaidAt?.UtcDateTime;
        _currentLockedRow.UpdatedAt = now;

        var paymentRow = PaymentRowMapper.ToNewRow(payment, invoice.Id.Value, now);
        db.Payments.Add(paymentRow);

        foreach (var outboxRow in outboxWriter.BuildRows(invoice.DomainEvents))
        {
            await InsertOutboxRowAsync(outboxRow, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: DuplicateKeyRow or DuplicateKeyConstraint })
        {
            // The belt-and-braces backstop (`R48`): the fast path's
            // `identityMatches` check cannot see a genuinely concurrent
            // request that raced past it — this is the LOSER of that race,
            // caught by the `payments.payment_reference` UNIQUE constraint
            // rather than by anything this transaction itself decided.
            db.Entry(paymentRow).State = EntityState.Detached;
            throw new PaymentReferenceConflictError(payment.PaymentReference);
        }

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
