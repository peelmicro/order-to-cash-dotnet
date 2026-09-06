using Microsoft.EntityFrameworkCore;
using OrderToCash.Billing.Application.Ports;
using OrderToCash.Billing.Domain;
using OrderToCash.Billing.Infrastructure.Outbox;
using OrderToCash.SharedKernel;
using RowCredit = OrderToCash.Billing.Infrastructure.Persistence.Entities.Credit;
using RowCreditItem = OrderToCash.Billing.Infrastructure.Persistence.Entities.CreditItem;

namespace OrderToCash.Billing.Infrastructure.Persistence;

/// <summary>
/// The lock protocol (design.md §5.5) + save + outbox drain (§7.2). Keeps
/// the tracked <c>credits</c> row and its loaded <c>credit_items</c> rows
/// for the one credit line locked through THIS instance — an identity map
/// scoped to one unit of work, the shape <c>EfCoreOrderRepository</c> and
/// <c>EfCoreStockItemRepository</c> already use.
/// </summary>
public sealed class EfCoreBuyerCreditRepository(BillingDbContext db, OutboxWriter outboxWriter, IClock clock) : IBuyerCreditRepository
{
    /// <summary>The credit-lock statement's literal column list, in the order <c>CreditConfiguration</c> declares them — every mapped column of <see cref="RowCredit"/>, because <c>FromSqlInterpolated</c> requires ALL of them in the projection. Exposed so <c>CreditClaimProjectionTests</c> can compare this list against the <c>IEntityType</c>'s mapped properties mechanically.</summary>
    public static readonly IReadOnlyList<string> CreditClaimColumnNames =
        ["id", "code", "retailer_code", "company_code", "credit_limit", "currency_code", "created_at", "updated_at"];

    /// <summary>The order-scoped <c>credit_items</c> read's literal column list — every mapped column of <see cref="RowCreditItem"/>.</summary>
    public static readonly IReadOnlyList<string> CreditItemClaimColumnNames =
        ["id", "credit_id", "order_reference", "amount", "type", "credit_date", "created_at", "updated_at"];

    private BuyerCredit? _current;
    private RowCredit? _currentRow;

    public async Task<BuyerCredit?> LockForOrderAsync(string retailerCode, string companyCode, OrderNumber orderReference, CancellationToken cancellationToken)
    {
        // Step 1 — the ONE row this transaction ever locks.
        var creditRow = await db.Credits
            .FromSqlInterpolated(
                $@"SELECT id, code, retailer_code, company_code, credit_limit, currency_code, created_at, updated_at
                   FROM   dbo.credits WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                   WHERE  retailer_code = {retailerCode} AND company_code = {companyCode}")
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (creditRow is null)
        {
            // BC3: no credit line exists. Nothing written, no fact.
            return null;
        }

        // Step 2 — the whole line's committed exposure, ONE scalar, no
        // GROUP BY (BC5). Executed as raw SQL — never db.CreditItems.SumAsync(...),
        // which LINQ-composes the hint away and hands back an unlocked
        // snapshot read (design.md §15 ledger L7).
#pragma warning disable EF1002 // creditRow.Id is a Guid this application already validated, never caller-supplied text
        var committedExposure = await db.Database
            .SqlQueryRaw<long>(
                @"SELECT COALESCE(SUM(CASE type WHEN 'hold' THEN amount WHEN 'release' THEN -amount ELSE 0 END), 0) AS Value
                  FROM   dbo.credit_items WITH (UPDLOCK, HOLDLOCK)
                  WHERE  credit_id = @creditId",
                new Microsoft.Data.SqlClient.SqlParameter("@creditId", creditRow.Id))
            .SingleAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002

        // Step 3 — the subject order's entries, an index seek on
        // (credit_id, order_reference) (B4/B5/BC7).
        var orderEntryRows = await db.CreditItems
            .FromSqlInterpolated(
                $@"SELECT id, credit_id, order_reference, amount, type, credit_date, created_at, updated_at
                   FROM   dbo.credit_items WITH (UPDLOCK, HOLDLOCK)
                   WHERE  credit_id = {creditRow.Id} AND order_reference = {orderReference.Value}")
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var snapshot = BuyerCreditRowMapper.ToDomain(creditRow, committedExposure, orderEntryRows);
        var credit = BuyerCredit.Reconstitute(snapshot);

        _current = credit;
        _currentRow = creditRow;

        return credit;
    }

    /// <summary>
    /// Adds one <c>credit_items</c> row per <see cref="BuyerCredit.AppendedEntries"/>
    /// (never an UPDATE, never a DELETE — <c>B2</c>), drains
    /// <c>credit.DomainEvents</c> into outbox rows — inserted ONE AWAITED
    /// statement at a time, copied verbatim from
    /// <c>EfCoreOrderRepository.InsertOutboxRowAsync</c> — then calls
    /// <c>SaveChangesAsync</c>, clearing domain events only after everything
    /// above returned (<c>OI9</c>). The <c>credits</c> row is NEVER written.
    /// </summary>
    public async Task SaveChangesAsync(BuyerCredit credit, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(credit, _current) || _currentRow is null)
        {
            throw new InvalidOperationException("SaveChangesAsync was called with an aggregate this repository did not load through LockForOrderAsync.");
        }

        var now = clock.UtcNow.UtcDateTime;

        foreach (var entry in credit.AppendedEntries)
        {
            db.CreditItems.Add(BuyerCreditRowMapper.ToNewRow(entry, _currentRow.Id, now));
        }

        foreach (var outboxRow in outboxWriter.BuildRows(credit.DomainEvents))
        {
            await InsertOutboxRowAsync(outboxRow, cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        credit.ClearDomainEvents();
    }

    /// <summary>Copied verbatim (reasoning and shape) from <c>EfCoreOrderRepository.InsertOutboxRowAsync</c> (ledger L16) — never <c>AddRange</c>.</summary>
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
