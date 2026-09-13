using System.Data;
using System.Text.Json;
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
    public async Task<EnqueueOutcome> EnqueueAsync(Guid orderId, string orderReference, SagaCommandKind command, string payload, Guid triggeringEventId, byte[]? triggeringEventEnvelope, string? triggeringEventTopic, CancellationToken cancellationToken)
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
            // observability_reliability, OR3/R29's dead-letter clause
            // (design.md §4.2) — the column is nvarchar(max), so the raw
            // bytes are decoded UTF-8 here, at the LAST possible moment
            // before storage, and re-encoded UTF-8 on the way out
            // (ToRecord below). Every fact envelope on this wire is
            // producer-emitted valid UTF-8 JSON, so the round trip is a
            // bijection — never a re-serialisation through Envelope<T>,
            // which would reorder keys and drop unknown fields (ledger L15).
            TriggeringEventEnvelope = triggeringEventEnvelope is null ? null : System.Text.Encoding.UTF8.GetString(triggeringEventEnvelope),
            TriggeringEventTopic = triggeringEventTopic,
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
                           status, attempts, last_error, next_attempt_at, created_at, updated_at, sent_at,
                           triggering_event_envelope, triggering_event_topic, dead_lettered_at
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
    public async Task<bool> ParkAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var current = await db.SagaCommands.AsNoTracking().SingleAsync(c => c.Id == commandId, cancellationToken).ConfigureAwait(false);

        var maxAttempts = Math.Max(1, options.Value.Command.MaxAttempts);
        var parkCycles = current.Attempts / maxAttempts;
        var backoffMs = Math.Min(30_000d * Math.Pow(2, parkCycles), options.Value.Sweeper.ParkRetryCapMs);
        var truncatedError = lastError.Length > MaxLastErrorLength ? lastError[..MaxLastErrorLength] : lastError;

        // Conditional on status <> 'sent' — mirrors #7's own notAlreadySent()
        // guard (drizzle-saga-command-store.ts:116-124): a row a concurrent
        // dispatcher has already reported `sent` is never overwritten
        // `parked`. The affected-row count is the "did this call actually
        // transition the row" answer OR3's first-park hook (observability_
        // reliability, design.md §4.4) gates on.
        var affected = await db.SagaCommands
            .Where(c => c.Id == commandId && c.Status != "sent")
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.Status, "parked")
                    .SetProperty(c => c.Attempts, current.Attempts + attemptsMade)
                    .SetProperty(c => c.LastError, truncatedError)
                    .SetProperty(c => c.NextAttemptAt, now.AddMilliseconds(backoffMs))
                    .SetProperty(c => c.UpdatedAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        return affected == 1;
    }

    /// <summary>
    /// Marks a claimed row <c>rejected</c> (feature 42) — the TERMINAL end
    /// state for a command whose responder replied with a business-rejection
    /// <c>RpcError</c>. Same accumulation shape as <see cref="ParkAsync"/>
    /// (never overwrites <c>attempts</c>), but sets <c>next_attempt_at</c>
    /// to <see langword="null"/> rather than scheduling a retry — there is
    /// none. <see cref="ClaimDueAsync"/>'s predicate (<c>pending</c> or
    /// <c>parked</c> only) structurally excludes a <c>rejected</c> row from
    /// every future sweep with no extra guard needed there.
    /// </summary>
    public async Task RejectAsync(Guid commandId, int attemptsMade, string lastError, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var current = await db.SagaCommands.AsNoTracking().SingleAsync(c => c.Id == commandId, cancellationToken).ConfigureAwait(false);

        var truncatedError = lastError.Length > MaxLastErrorLength ? lastError[..MaxLastErrorLength] : lastError;

        await db.SagaCommands
            .Where(c => c.Id == commandId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(c => c.Status, "rejected")
                    .SetProperty(c => c.Attempts, current.Attempts + attemptsMade)
                    .SetProperty(c => c.LastError, truncatedError)
                    .SetProperty(c => c.NextAttemptAt, (DateTime?)null)
                    .SetProperty(c => c.UpdatedAt, now),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// <c>OR3</c>'s "at most once per row" claim (design.md §4.3) — ONE
    /// <c>UPDATE ... WHERE dead_lettered_at IS NULL</c>, never a
    /// <c>SELECT</c> then an <c>UPDATE</c>/<c>INSERT</c>. The affected-row
    /// count is the answer: exactly one caller can ever see
    /// <see langword="true"/> for a given row, no matter how many callers
    /// race it — a check-then-act rewrite (read the column, then decide
    /// whether to write) lets two racing callers both observe <c>NULL</c>
    /// and both win, which is exactly the defect shape <c>CLAUDE.md</c>
    /// records as shipped once already in this repository.
    /// </summary>
    public async Task<bool> TryClaimDeadLetterAsync(Guid commandId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;

        var affected = await db.SagaCommands
            .Where(c => c.Id == commandId && c.DeadLetteredAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(c => c.DeadLetteredAt, now),
                cancellationToken)
            .ConfigureAwait(false);

        return affected == 1;
    }

    /// <summary>
    /// Feature <c>operator_note_survives_the_compensation_branches</c> (id
    /// 71) — checks <c>credit.release</c> before <c>stock.release</c>
    /// (arbitrary but fixed precedence for the "both rows carry a synthetic
    /// envelope" defensive case — <see cref="SagaCommandStoreTests"/>'s own
    /// precedence test pins it); selection between the two is by envelope
    /// CONTENT (<see cref="ExtractOperatorCancelNote"/>), never by which
    /// command name or insertion position happens to carry the synthetic
    /// envelope — under SA-4 that is <c>stock.release</c> for BOTH the
    /// <c>stock_reserved</c> and the <c>credit_approved</c>/<c>confirmed</c>
    /// branch (<see cref="ISagaCommandStore.FindOperatorCancelNoteAsync"/>'s
    /// own remarks). A bare <c>AsNoTracking</c> read, never a claim/lease,
    /// so it never contends with <see cref="TryClaimAsync"/>/<see cref="ClaimDueAsync"/>.
    /// </summary>
    public async Task<string?> FindOperatorCancelNoteAsync(Guid orderId, CancellationToken cancellationToken)
    {
        const string CreditReleaseToken = "credit.release";
        const string StockReleaseToken = "stock.release";

        var rows = await db.SagaCommands
            .AsNoTracking()
            .Where(c => c.OrderId == orderId && (c.Command == CreditReleaseToken || c.Command == StockReleaseToken))
            .Select(c => new { c.Command, c.TriggeringEventEnvelope })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var creditReleaseEnvelope = rows.FirstOrDefault(r => r.Command == CreditReleaseToken)?.TriggeringEventEnvelope;
        var stockReleaseEnvelope = rows.FirstOrDefault(r => r.Command == StockReleaseToken)?.TriggeringEventEnvelope;

        return ExtractOperatorCancelNote(creditReleaseEnvelope) ?? ExtractOperatorCancelNote(stockReleaseEnvelope);
    }

    /// <summary>
    /// <see langword="null"/> for anything that is not the synthetic
    /// <c>orders.cancel.requested</c> envelope — a <see langword="null"/>
    /// column (a row enqueued before this feature, or a saga-decided
    /// <c>credit.rejected.v1</c>/<c>stock.rejected.v1</c> row whose
    /// envelope is a REAL fact), a real fact envelope of any other
    /// <c>eventType</c>, or the synthetic envelope with no <c>note</c> key
    /// (JsonWire's own null-omission — the operator supplied none).
    /// </summary>
    private static string? ExtractOperatorCancelNote(string? envelopeJson) =>
        IsOperatorCancelEnvelope(envelopeJson, out var root) && root.TryGetProperty("payload", out var payload) && payload.TryGetProperty("note", out var note)
            ? note.GetString()
            : null;

    /// <summary>
    /// Content, never position or command name, decides whether
    /// <paramref name="envelopeJson"/> is the synthetic
    /// <c>orders.cancel.requested</c> envelope
    /// (<see cref="Application.Commands.CancelOrderCommandHandler"/>'s own
    /// direct enqueue) rather than a real fact's bytes — shared by
    /// <see cref="ExtractOperatorCancelNote"/> and
    /// <see cref="HasAcceptedOperatorCancelAsync"/> so both read the SAME
    /// content test, never two independently-maintained ones.
    /// </summary>
    private static bool IsOperatorCancelEnvelope(string? envelopeJson, out JsonElement root)
    {
        if (envelopeJson is null)
        {
            root = default;
            return false;
        }

        using var document = JsonDocument.Parse(envelopeJson);
        root = document.RootElement.Clone();
        return root.TryGetProperty("eventType", out var eventType) && eventType.GetString() == "orders.cancel.requested";
    }

    /// <summary>
    /// Id 62 (SA-4) — an <c>EXISTS</c>-shaped check over <c>credit.release</c>
    /// and <c>stock.release</c> rows for <paramref name="orderId"/>,
    /// narrowed to envelope CONTENT (<see cref="IsOperatorCancelEnvelope"/>):
    /// a row carrying the synthetic <c>orders.cancel.requested</c> envelope
    /// means an operator cancellation has been accepted; a row of either
    /// command carrying a REAL fact's envelope (R27's <c>credit_rejected</c>
    /// path also enqueues <c>stock.release</c>) must not count — armed by
    /// substituting "any <c>stock.release</c> row" (record's arming table,
    /// A3). No status filter: a <c>sent</c> or even <c>rejected</c> row
    /// still means "an operator cancellation was requested for this order".
    /// Reads through the SAME ambient <see cref="OrdersDbContext"/> every
    /// other method here uses — under this database's
    /// <c>READ_COMMITTED_SNAPSHOT ON</c>, executed AFTER the caller's own
    /// <c>UPDLOCK</c> read of the order row
    /// (<see cref="Persistence.EfCoreOrderRepository.GetByIdAsync"/>) has
    /// already waited out any concurrent enqueue for the SAME order, so this
    /// statement's own snapshot is guaranteed fresh relative to it.
    /// </summary>
    public async Task<bool> HasAcceptedOperatorCancelAsync(Guid orderId, CancellationToken cancellationToken)
    {
        const string CreditReleaseToken = "credit.release";
        const string StockReleaseToken = "stock.release";

        var envelopes = await db.SagaCommands
            .AsNoTracking()
            .Where(c => c.OrderId == orderId && (c.Command == CreditReleaseToken || c.Command == StockReleaseToken))
            .Select(c => c.TriggeringEventEnvelope)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return envelopes.Any(envelope => IsOperatorCancelEnvelope(envelope, out _));
    }

    private static SagaCommandRecord ToRecord(SagaCommand row) => new(
        row.Id,
        row.OrderId,
        row.OrderReference,
        SagaCommandKinds.Parse(row.Command),
        row.Payload,
        row.TriggeringEventId,
        row.Attempts,
        row.TriggeringEventEnvelope is null ? null : System.Text.Encoding.UTF8.GetBytes(row.TriggeringEventEnvelope),
        row.TriggeringEventTopic);
}
