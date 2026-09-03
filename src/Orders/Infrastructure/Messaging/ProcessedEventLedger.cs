// CANONICAL COPY — feature outbox_and_idempotency, design.md §6.3-§6.4.
// This file is the reference every consuming write model's own dedup ledger
// copies VERBATIM (features 17-24 copy rather than fork it). Two regions are
// normalised for the parity check (tests/Orders.UnitTests/
// IdempotentConsumerParityTests.cs, OI12): the leading banner you are
// reading now (every contiguous `//`/`///` line up to the first line that is
// neither), and the single `namespace` declaration below. Outside those two
// regions this file must never name a service (`Orders`, `Fulfillment`,
// `Billing`, `Projector`, `Notifications`, in any casing) and every `using`
// must resolve to a namespace on the whitelist design.md §6.4 fixes —
// Microsoft.EntityFrameworkCore, Microsoft.Data.SqlClient,
// OrderToCash.SharedKernel, or the copying service's own `.Application.Ports`
// / `.Infrastructure.Persistence.Entities` namespace, matched by suffix. A
// copy that fails either constraint is not adoptable, and the guard is
// IdempotentConsumerParityTests' case 2.
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Messaging;

/// <summary>The outcome of one dedup-record insert attempt.</summary>
public enum LedgerInsertOutcome
{
    Inserted,
    Duplicate,
}

/// <summary>
/// The <c>processed_events</c> insert, inside the AMBIENT transaction —
/// takes <see cref="DbContext"/>, never a service-specific subtype, and
/// resolves the entity through <c>db.Set&lt;ProcessedEvent&gt;()</c> rather
/// than a typed <c>DbSet</c> property, because a typed property would force
/// this file to name a service-specific context type. MS-SQL error 2601
/// ("cannot insert duplicate key row in object with unique index") and 2627
/// (its constraint-shaped sibling) both surface as
/// <see cref="LedgerInsertOutcome.Duplicate"/>; anything else propagates
/// unchanged — swallowing an unrelated failure as "duplicate" would turn a
/// real error into a silent acknowledgement, which is the one outcome R18
/// must never produce by accident.
/// </summary>
public sealed class ProcessedEventLedger
{
    private const int DuplicateKeyRow = 2601;
    private const int DuplicateKeyConstraint = 2627;

    public async Task<LedgerInsertOutcome> TryInsertAsync(DbContext db, Guid eventId, string consumer, DateTime processedAt, CancellationToken cancellationToken)
    {
        var entry = new ProcessedEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Consumer = consumer,
            ProcessedAt = processedAt,
            CreatedAt = processedAt,
        };

        db.Set<ProcessedEvent>().Add(entry);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return LedgerInsertOutcome.Inserted;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: DuplicateKeyRow or DuplicateKeyConstraint })
        {
            // The change tracker is poisoned by the failure: the entry is
            // still `Added`, and reusing this context would re-attempt the
            // insert on the next save. Detach it so the caller's own
            // transaction rollback (design.md §6.1) is the only cleanup
            // needed.
            db.Entry(entry).State = EntityState.Detached;
            return LedgerInsertOutcome.Duplicate;
        }
    }
}
