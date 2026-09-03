// CANONICAL COPY — feature outbox_and_idempotency, design.md §6.1-§6.4.
// This file is the reference every consuming write model's own dedup
// primitive copies VERBATIM (features 17-24 copy rather than fork it). Two
// regions are normalised for the parity check (tests/Orders.UnitTests/
// IdempotentConsumerParityTests.cs, OI12): the leading banner you are
// reading now (every contiguous `//`/`///` line up to the first line that is
// neither), and the single `namespace` declaration below. Outside those two
// regions this file must never name a service (`Orders`, `Fulfillment`,
// `Billing`, `Projector`, `Notifications`, in any casing) and every `using`
// must resolve to a namespace on the whitelist design.md §6.4 fixes —
// Microsoft.EntityFrameworkCore, Microsoft.Data.SqlClient,
// OrderToCash.SharedKernel, or the copying service's own `.Application.Ports`
// / `.Infrastructure.Persistence.Entities` namespace, matched by suffix
// rather than by literal text (which is why `IUnitOfWork`, `IClock` and
// `ConsumerName` — per-service files at identical paths in every write
// model — may be referenced here). A copy that fails either constraint is
// not adoptable, and the guard is IdempotentConsumerParityTests' case 2.
using Microsoft.EntityFrameworkCore;
using OrderToCash.Orders.Application.Ports;

namespace OrderToCash.Orders.Infrastructure.Messaging;

/// <summary>Whether this delivery ran the handler's effects, or was already recorded and did nothing.</summary>
public enum ConsumptionOutcome
{
    Processed,
    Duplicate,
}

/// <summary>
/// Runs <paramref name="work"/> AT MOST ONCE for (<c>eventId</c>,
/// <c>consumer</c>) — design.md §6.1, R17, R18. The dedup record is
/// inserted FIRST, inside <see cref="IUnitOfWork.ExecuteAsync{T}"/>'s
/// transaction, and only if that insert succeeds does <paramref name="work"/>
/// run — a duplicate is detected by the unique-index violation
/// <see cref="ProcessedEventLedger"/> surfaces, never by a
/// <c>SELECT</c>-then-<c>INSERT</c> check, which would let two concurrent
/// deliveries of the same event both through under READ COMMITTED. There is
/// no <c>SELECT</c> anywhere in the dedup path — the unique index is the
/// whole guarantee.
/// </summary>
public sealed class IdempotentConsumer(IUnitOfWork unitOfWork, IClock clock, ProcessedEventLedger ledger, DbContext db)
{
    public async Task<ConsumptionOutcome> RunOnceAsync(
        Guid eventId,
        ConsumerName consumer,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteAsync(
                async ct =>
                {
                    var insertOutcome = await ledger.TryInsertAsync(db, eventId, ConsumerNames.ToToken(consumer), clock.UtcNow.UtcDateTime, ct);

                    if (insertOutcome == LedgerInsertOutcome.Duplicate)
                    {
                        // Throwing (rather than just returning Duplicate) is
                        // what makes IUnitOfWork.ExecuteAsync roll back
                        // instead of committing an empty transaction — R18's
                        // "no mutation, no fact, no command" together with
                        // the dedup attempt itself leaving no trace. work is
                        // never called on this path.
                        throw new DuplicateEventException();
                    }

                    await work(ct);
                    return ConsumptionOutcome.Processed;
                },
                cancellationToken);
        }
        catch (DuplicateEventException)
        {
            return ConsumptionOutcome.Duplicate;
        }
    }

    /// <summary>Signals the duplicate branch across the <see cref="IUnitOfWork"/> boundary — never observed outside this file.</summary>
    private sealed class DuplicateEventException : Exception;
}
