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
public sealed class EfCoreOrderNumberAllocator(OrdersDbContext db) : IOrderNumberAllocator
{
    public async Task<OrderNumber> AllocateNextAsync(CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             IF NOT EXISTS (SELECT 1 FROM dbo.order_number_sequences WHERE id = 1)
             BEGIN
                 DECLARE @start int = (
                     SELECT ISNULL(MAX(CAST(SUBSTRING(order_reference, {OrderNumber.Prefix.Length + 1}, LEN(order_reference) - {OrderNumber.Prefix.Length}) AS int)), 0) + 1
                     FROM dbo.orders);
                 INSERT INTO dbo.order_number_sequences (id, next_value) VALUES (1, @start);
             END
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
