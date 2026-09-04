using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.Orders.Infrastructure.Persistence;
using OrderToCash.Orders.Infrastructure.Persistence.Entities;

namespace OrderToCash.Orders.Infrastructure.Saga;

/// <summary>
/// The <c>saga_commands</c> adapter (design.md §6.3) — enqueue (ambient
/// transaction), claim (SO11's bounded lease), claim-due (the sweeper's
/// batch), mark-sent, park. No new table, no new column, no migration —
/// <c>SagaCommand</c> and its configuration already exist (design.md §7).
/// </summary>
public sealed class EfCoreSagaCommandStore(OrdersDbContext db, IClock clock, IOptions<OrdersSagaOptions> options) : ISagaCommandStore
{
    private const int DuplicateKeyRow = 2601;
    private const int DuplicateKeyConstraint = 2627;
    private const int MaxLastErrorLength = 4_000;

    /// <summary>Inserts a <c>pending</c> row through the AMBIENT <see cref="OrdersDbContext"/> — no <c>tx</c> parameter, matching <see cref="Persistence.EfCoreOrderRepository"/>'s own shape. A duplicate-key hit on <c>(order_id, command)</c> means the command is already owed or already sent (SO3) — caught exactly as <c>ProcessedEventLedger</c> catches its own, detached, and reported as <see cref="EnqueueOutcome.AlreadyEnqueued"/> rather than propagated.</summary>
    public async Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var row = new SagaCommand
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            OrderReference = orderReference,
            Command = SagaCommandKinds.ToToken(command),
            Payload = payload,
            TriggeringEventId = triggeringEventId,
            Status = "pending",
            Attempts = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.SagaCommands.Add(row);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return EnqueueOutcome.Enqueued;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: DuplicateKeyRow or DuplicateKeyConstraint })
        {
            // The change tracker is poisoned by the failure — detach so the
            // caller's own ambient-transaction rollback (if the surrounding
            // work throws for an unrelated reason) is the only cleanup
            // needed, exactly as ProcessedEventLedger does.
            db.Entry(row).State = EntityState.Detached;
            return EnqueueOutcome.AlreadyEnqueued;
        }
    }

    /// <summary>
    /// A single conditional <c>UPDATE</c> (design.md §6.3) claiming the one
    /// row for <paramref name="orderId"/>/<paramref name="command"/> if — and
    /// only if — it is currently <c>pending</c> or <c>parked</c> AND not
    /// already under another claimant's lease. Zero rows affected is a
    /// SILENT no-op: returns <see langword="null"/>, never throws.
    /// </summary>
    public async Task<SagaCommandRecord?> TryClaimAsync(Guid orderId, SagaCommandKind command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var leaseUntil = now.AddMilliseconds(options.Value.Command.LeaseMs);
        var commandToken = SagaCommandKinds.ToToken(command);

        var affected = await db.SagaCommands
            .Where(c => c.OrderId == orderId
                     && c.Command == commandToken
                     && (c.Status == "pending" || c.Status == "parked")
                     && (c.NextAttemptAt == null || c.NextAttemptAt <= now))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.NextAttemptAt, leaseUntil)
                    .SetProperty(c => c.UpdatedAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            return null;
        }

        var claimed = await db.SagaCommands
            .AsNoTracking()
            .SingleAsync(c => c.OrderId == orderId && c.Command == commandToken, cancellationToken)
            .ConfigureAwait(false);

        return ToRecord(claimed);
    }

    /// <summary>
    /// The sweeper's batch claim (design.md §6.4): every <c>pending</c> row
    /// past <see cref="OrdersSagaSweeperOptions.PendingGraceMs"/> (the SO3
    /// crash window) and every due <c>parked</c> row, up to
    /// <paramref name="batchSize"/>. Claimed under
    /// <c>WITH (UPDLOCK, READPAST, ROWLOCK)</c> — measured (feature 14) to
    /// skip rather than block under this database's
    /// <c>READ_COMMITTED_SNAPSHOT ON</c> — in one short transaction that
    /// both selects and stamps the lease, so a concurrent sweeper (or a
    /// concurrent <see cref="TryClaimAsync"/>) never double-claims a row.
    /// </summary>
    public async Task<IReadOnlyList<SagaCommandRecord>> ClaimDueAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var pendingCutoff = now.AddMilliseconds(-options.Value.Sweeper.PendingGraceMs);
        var leaseUntil = now.AddMilliseconds(options.Value.Command.LeaseMs);

        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

#pragma warning disable EF1002 // batchSize is a validated int; pendingCutoff/now are bound parameters — never caller-supplied text.
            var claimed = await db.SagaCommands
                .FromSqlInterpolated($@"
                    SELECT TOP ({batchSize})
                           id, order_id, order_reference, command, payload, triggering_event_id,
                           status, attempts, last_error, next_attempt_at, created_at, updated_at, sent_at
                    FROM   dbo.saga_commands WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE  (status = 'pending' AND created_at <= {pendingCutoff} AND (next_attempt_at IS NULL OR next_attempt_at <= {now}))
                        OR (status = 'parked' AND next_attempt_at <= {now})
                    ORDER  BY created_at ASC")
#pragma warning restore EF1002
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (claimed.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return [];
            }

            var ids = claimed.Select(row => row.Id).ToList();

            await db.SagaCommands
                .Where(c => ids.Contains(c.Id))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(c => c.NextAttemptAt, leaseUntil)
                        .SetProperty(c => c.UpdatedAt, now),
                    cancellationToken)
                .ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            return (IReadOnlyList<SagaCommandRecord>)claimed.Select(ToRecord).ToList();
        }).ConfigureAwait(false);
    }

    /// <summary>Marks a claimed row <c>sent</c> — a reply was delivered, including a business rejection (SO6). Never means "the saga advanced".</summary>
    public async Task MarkSentAsync(Guid commandId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;

        await db.SagaCommands
            .Where(c => c.Id == commandId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.Status, "sent")
                    .SetProperty(c => c.SentAt, now)
                    .SetProperty(c => c.NextAttemptAt, (DateTime?)null)
                    .SetProperty(c => c.UpdatedAt, now),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Marks a claimed row <c>parked</c> on exhaustion (SO5): ACCUMULATES
    /// <c>attempts</c> (never overwrites — the count must survive every
    /// later park cycle for the operator view to be honest), truncates
    /// <c>last_error</c>, and schedules <c>next_attempt_at</c> on capped
    /// exponential backoff. There is no separate "park cycle" column, so the
    /// cycle count is derived from the attempts already accumulated before
    /// THIS park, divided by the in-line policy's own
    /// <see cref="OrdersSagaCommandOptions.MaxAttempts"/> — safe because
    /// every caller of this method (the dispatch worker, the sweeper) drives
    /// the SAME <c>SagaCommandDispatcher</c> with the SAME policy.
    /// </summary>
    public async Task ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var current = await db.SagaCommands.AsNoTracking().SingleAsync(c => c.Id == commandId, cancellationToken).ConfigureAwait(false);

        var maxAttempts = Math.Max(1, options.Value.Command.MaxAttempts);
        var parkCycles = current.Attempts / maxAttempts;
        var backoffMs = Math.Min(30_000d * Math.Pow(2, parkCycles), options.Value.Sweeper.ParkRetryCapMs);
        var truncatedError = lastError.Length > MaxLastErrorLength ? lastError[..MaxLastErrorLength] : lastError;

        await db.SagaCommands
            .Where(c => c.Id == commandId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.Status, "parked")
                    .SetProperty(c => c.Attempts, current.Attempts + attemptsMade)
                    .SetProperty(c => c.LastError, truncatedError)
                    .SetProperty(c => c.NextAttemptAt, now.AddMilliseconds(backoffMs))
                    .SetProperty(c => c.UpdatedAt, now),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static SagaCommandRecord ToRecord(SagaCommand row) => new(
        row.Id,
        row.OrderId,
        row.OrderReference,
        SagaCommandKinds.Parse(row.Command),
        row.Payload,
        row.TriggeringEventId,
        row.Attempts);
}
