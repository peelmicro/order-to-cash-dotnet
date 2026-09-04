using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Infrastructure.Persistence;

/// <summary>
/// Allocates <c>ORD-######</c> under <c>WITH (UPDLOCK, ROWLOCK)</c> on
/// <c>dbo.order_number_sequences</c>' single row (<c>id = 1</c>) —
/// <c>OrderNumberSequenceConfiguration</c>'s own remark: "Allocation under a
/// row lock (<c>UPDLOCK</c>) is a repository concern, out of scope [t]here".
/// Two callers racing serialise on the exclusive row lock the first
/// statement below takes: the second blocks until the first commits
/// (releasing the lock, per <see cref="IUnitOfWork"/>'s ambient transaction)
/// or rolls back, so no two callers ever read the same <c>next_value</c>.
/// </summary>
/// <remarks>
/// <b>Self-initialising</b>, matching #7's own
/// <c>order-number-allocator.ts</c> reasoning verbatim: the migration
/// creates <c>order_number_sequences</c> empty (no <c>HasData</c>) and the
/// seed writes no row into it either, so the FIRST ever allocation must
/// seed the counter from <c>MAX(order_reference)</c> over the already-seeded
/// orders rather than assume a hardcoded starting value — otherwise a fresh
/// Testcontainers database and the seeded compose stack would need two
/// different starting points. The numeric suffix is CAST to <c>int</c>
/// before <c>MAX()</c>, not compared as a string, for the identical reason
/// #7's own review flagged (its D6): a lexical <c>MAX</c> on
/// zero-padded-but-variable-width text goes backwards once the sequence
/// crosses seven digits.
/// </remarks>
/// <remarks>
/// <b>Feature 45.</b> The seed is a single atomic <c>INSERT ... SELECT ...
/// WHERE NOT EXISTS (... WITH (UPDLOCK, HOLDLOCK) ...)</c>, not the
/// check-then-act <c>IF NOT EXISTS ... INSERT</c> feature 15 shipped. #7's
/// idiom (<c>INSERT ... ON DUPLICATE KEY UPDATE</c>, run unconditionally on
/// every call, where only the first insert does anything) is atomic because
/// MySQL's statement itself is the unit of atomicity — there is no separate
/// check to race. MS-SQL has no single-statement upsert with that property;
/// <c>IF NOT EXISTS (SELECT 1 ...) BEGIN INSERT ... END</c> is TWO
/// statements (an unlocked read, then a write), and under
/// <c>READ_COMMITTED_SNAPSHOT</c> — on for every database here, see
/// <c>infra/mssql/init/01-create-databases.sql</c> — a plain read takes no
/// lock at all, so two callers can both see "not exists" as true before
/// either commits its insert; the loser gets a primary-key violation. That
/// is the race this feature closes, and it is a lost-in-translation defect
/// (feature 16's review, A1), not a spec gap: #7's committed idiom was
/// never unsafe, only the check-then-act rendering of it was.
///
/// <c>WITH (UPDLOCK, HOLDLOCK)</c> on the existence check forces a real
/// lock regardless of RCSI — table hints override the ambient
/// row-versioning read and take an actual key-range lock on the (empty)
/// slot where <c>id = 1</c> would sit, exactly SQL Server's documented
/// "insert if not exists" idiom. <c>HOLDLOCK</c> (a session-scoped
/// SERIALIZABLE hint for this one reference) holds that range lock until
/// the ambient transaction ends, so a second caller's own existence check
/// blocks behind it rather than racing it, and re-evaluates once the first
/// caller's insert has committed (finds the row, does not insert) or rolled
/// back (still missing, inserts). Locks taken by the same session/
/// transaction never self-block, so the allocator's own later claiming
/// <c>SELECT ... WITH (UPDLOCK, ROWLOCK)</c> below is unaffected.
///
/// The aggregate (<c>MAX(...)</c> over <c>dbo.orders</c>) is computed in a
/// derived table, not inline in the outer <c>SELECT</c>'s <c>WHERE</c>: an
/// aggregate with no <c>GROUP BY</c> always returns exactly one row even
/// when its own <c>FROM</c> is empty, so folding <c>WHERE NOT EXISTS</c>
/// into that same aggregating query would not suppress the insert once the
/// row already exists — it would attempt one on every single call and fail
/// with a duplicate-key violation from the second call onward. Wrapping the
/// aggregate in a one-row derived table and filtering THAT with
/// <c>WHERE NOT EXISTS</c> keeps the row count conditional on the
/// existence check, matching #7's own "compute the candidate value every
/// call, only the first insert lands" behaviour.
///
/// <b>Rejected: <c>MERGE ... WITH (HOLDLOCK)</c>.</b> Looks like the
/// canonical MS-SQL upsert, but <c>MERGE</c> has a well-documented
/// optimizer gap where the matching and the insert are not always
/// serialised the way <c>HOLDLOCK</c> implies, and concurrent <c>MERGE</c>
/// statements against the same key are known to still throw duplicate-key
/// violations in practice (Microsoft Connect items, and Aaron Bertrand's
/// "Use Caution with SQL Server's MERGE Statement" cover the same failure
/// mode this feature exists to close). Reaching for the statement most
/// associated with exactly this race is the wrong lesson to draw from it.
///
/// <b>Rejected: attempt the insert, catch 2627/2601, retry.</b> Correct in
/// outline, but it turns one SQL statement into C# control flow with a
/// retry loop, and it is not obviously composable with the ambient
/// transaction <see cref="IUnitOfWork"/> already has open here (default
/// <c>XACT_ABORT OFF</c> makes a caught statement-level error continuable
/// on this connection, but that is an ambient session setting to depend on
/// silently, not a property of the statement itself). The locking idiom
/// keeps the seed a single statement with the same atomicity guarantee and
/// no new failure mode to reason about.
///
/// Under true <c>SNAPSHOT</c> transaction isolation (not RCSI, which is a
/// database-level default for un-hinted reads) explicit locking hints on a
/// snapshot transaction can conflict with the engine's write-conflict
/// detection instead of blocking. Nothing here opens a transaction at that
/// isolation level — <see cref="IUnitOfWork"/> and every test below use
/// <c>ReadCommitted</c> — so this is a boundary this feature does not need
/// to cross, not a gap it leaves open.
/// </remarks>
public sealed class EfCoreOrderNumberAllocator(OrdersDbContext db) : IOrderNumberAllocator
{
    public async Task<OrderNumber> AllocateNextAsync(CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO dbo.order_number_sequences (id, next_value)
             SELECT 1, seed.next_value
             FROM (
                 SELECT ISNULL(MAX(CAST(SUBSTRING(order_reference, {OrderNumber.Prefix.Length + 1}, LEN(order_reference) - {OrderNumber.Prefix.Length}) AS int)), 0) + 1 AS next_value
                 FROM dbo.orders
             ) AS seed
             WHERE NOT EXISTS (
                 SELECT 1 FROM dbo.order_number_sequences WITH (UPDLOCK, HOLDLOCK) WHERE id = 1
             )
             """,
            cancellationToken).ConfigureAwait(false);

        var sequenceRow = await db.OrderNumberSequences
            .FromSqlRaw("SELECT * FROM dbo.order_number_sequences WITH (UPDLOCK, ROWLOCK) WHERE id = 1")
            .SingleAsync(cancellationToken).ConfigureAwait(false);

        var allocated = sequenceRow.NextValue;
        sequenceRow.NextValue = allocated + 1;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new OrderNumber(allocated);
    }
}
