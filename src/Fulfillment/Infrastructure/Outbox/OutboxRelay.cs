// COPY OF — src/Orders/Infrastructure/Outbox/OutboxRelay.cs
using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Infrastructure.Observability;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;
using WriteModelDbContext = OrderToCash.Fulfillment.Infrastructure.Persistence.FulfillmentDbContext;

namespace OrderToCash.Fulfillment.Infrastructure.Outbox;

/// <summary>Claimed vs. actually stamped, so a caller (and a test) can tell a "nothing to do" cycle from a "claimed but the publish failed" one.</summary>
public sealed record OutboxRelayResult(int Claimed, int Published);

/// <summary>
/// The one member <see cref="OutboxRelayBackgroundService"/> depends on —
/// resolved from DI rather than the concrete <see cref="OutboxRelay"/>
/// class, so <c>OutboxRelayLoopTests</c> (OI6) can substitute a
/// controllable fake and prove the loop's OWN re-entry and drain behaviour
/// with no database and no host (design.md §9.1: "unit | none").
/// </summary>
public interface IOutboxRelay
{
    Task<OutboxRelayResult> RunOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
/// <c>RunOnceAsync()</c> = claim -&gt; publish -&gt; stamp, in ONE write-model
/// transaction (design.md §5.1 – §5.3). A plain class — no base type, no
/// attribute — deriving from nothing, so it is directly callable from a test
/// with no host (design.md §2.2).
/// </summary>
public sealed class OutboxRelay(
    WriteModelDbContext db,
    IFactPublisher publisher,
    IClock clock,
    IOptions<OutboxRelayOptions> options,
    IDlqDepthGauge dlqDepthGauge,
    ILogger<OutboxRelay> logger) : IOutboxRelay
{
    /// <summary>
    /// The claim's column list, in the order <c>OutboxMessageConfiguration</c>
    /// declares them — every mapped column of <see cref="OutboxMessage"/>,
    /// because <c>FromSqlInterpolated</c> requires ALL of them in the
    /// projection. Exposed so <c>OutboxClaimProjectionTests</c> (E7) can
    /// compare this list against the <c>IEntityType</c>'s mapped properties
    /// mechanically, rather than by inspection — a missing column here would
    /// otherwise be a runtime error, not a compile error.
    /// </summary>
    public static readonly IReadOnlyList<string> ClaimColumnNames =
    [
        "id", "event_id", "event_type", "aggregate_id", "correlation_id", "causation_id",
        "payload", "occurred_at", "published_at", "created_at", "seq", "trace_parent",
    ];

    public async Task<OutboxRelayResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        // OR5/design.md §7 — otc_outbox_lag_ms and otc_dlq_depth, recorded
        // ONCE per cycle, OUTSIDE the claim transaction below so neither
        // round trip (a plain read; a Kafka admin/watermark round trip)
        // extends the claim's UPDLOCK hold time.
        await RecordOutboxLagAsync(cancellationToken).ConfigureAwait(false);
        await dlqDepthGauge.RecordAsync(cancellationToken).ConfigureAwait(false);

        var relayOptions = options.Value;

        // Backlog id 87 — NOT db.Database.CreateExecutionStrategy(): this
        // write model's plain UseSqlServer(...) registration (no
        // EnableRetryOnFailure — the unit-of-work's own remarks forbid
        // enabling it there) makes that call return a non-retrying
        // strategy whose ExecuteAsync only WRAPS a transient SqlException
        // (deadlock victim, error 1205, included) in an
        // InvalidOperationException before rethrowing it — never retrying.
        // DeadlockRetryExecutionStrategy is scoped to THIS call site alone:
        // it changes nothing about the write model's own registration, so
        // the unit-of-work's transactions are unaffected. See its own
        // remarks for the full mechanism and why retrying this delegate on
        // error 1205 is safe.
        var strategy = new DeadlockRetryExecutionStrategy(db);

        return await strategy.ExecuteAsync(async () =>
        {
            // The relay's own transaction (design.md §5.1) — not
            // IUnitOfWork: the relay writes no aggregate and enlists no
            // other collaborator, and the claim's table hints need the
            // isolation level pinned at the point they are taken.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            // design.md §5.2: UPDLOCK takes the update lock and holds it to
            // the end of the transaction; READPAST skips rows another
            // transaction already holds instead of waiting for them (MS-SQL
            // has no SKIP LOCKED); ROWLOCK asks for row granularity so a
            // multi-row claim does not start life as a page lock.
            // FromSqlInterpolated requires every mapped column of the
            // entity type in the projection — OutboxClaimProjectionTests
            // (E7) asserts this list stays complete.
            // The column list is written LITERALLY here — never as an
            // interpolation hole — because every `{...}` inside a
            // FromSqlInterpolated string becomes a bound SQL PARAMETER, and
            // a column list cannot be a parameter value. Only @batchSize
            // (below) is genuinely dynamic. This literal list is kept in
            // sync with ClaimColumnNames above by OutboxClaimProjectionTests
            // (E7), which compares BOTH against the entity model.
#pragma warning disable EF1002 // the batch size is a validated int, never caller-supplied text; the rest of the string is a fixed literal, never concatenated user input
            var claimed = await db.OutboxMessages
                .FromSqlInterpolated($@"
                    SELECT TOP ({relayOptions.BatchSize})
                           id, event_id, event_type, aggregate_id, correlation_id, causation_id,
                           payload, occurred_at, published_at, created_at, seq, trace_parent
                    FROM   dbo.outbox WITH (UPDLOCK, READPAST, ROWLOCK)
                    WHERE  published_at IS NULL
                    ORDER  BY seq ASC")
#pragma warning restore EF1002
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            if (claimed.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return new OutboxRelayResult(0, 0);
            }

            var facts = claimed.Select(BuildPublishableFact).ToList();

            // OI14: bounds the publish so an unreachable broker cannot hold
            // this claim's UPDLOCKs open indefinitely.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(relayOptions.PublishTimeoutMs);

            try
            {
                await publisher.PublishAsync(facts, timeoutCts.Token);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                // A genuine outer cancellation (host shutdown) is NOT caught
                // here — it propagates so the caller (the BackgroundService
                // loop, design.md §5.4) can tell it apart from an ordinary
                // failed cycle. A publish failure OR our own timeout firing
                // both land here: OI8/OI14 — roll back rather than commit
                // empty, leave every claimed record unstamped, retry the
                // same records on the next poll.
                //
                // design.md §6's scope-push table, OutboxRelay row — the
                // row's own correlation_id, pushed PER CLAIMED ROW rather
                // than once for the whole batch, so a reader filtering logs
                // by correlationId sees every relay cycle that touched it.
                foreach (var row in claimed)
                {
                    using var correlationScope = logger.BeginScope(new Dictionary<string, object> { ["correlation_id"] = row.CorrelationId });
                    logger.LogError(ex, "Outbox relay failed to publish {EventId} ({EventType}): {Message}", row.EventId, row.EventType, ex.Message);
                }

                await transaction.RollbackAsync(cancellationToken);
                return new OutboxRelayResult(claimed.Count, 0);
            }

            var ids = claimed.Select(row => row.Id).ToList();
            var now = clock.UtcNow.UtcDateTime;

            // Set-based — does not go through the change tracker, matching
            // AsNoTracking() above (design.md §5.2's "How it is expressed in
            // EF Core").
            var stamped = await db.OutboxMessages
                .Where(row => ids.Contains(row.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedAt, now), cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new OutboxRelayResult(claimed.Count, stamped);
        });
    }

    /// <summary>
    /// otc_outbox_lag_ms — the age, in milliseconds, of the OLDEST row with
    /// <c>published_at IS NULL</c>, via the existing <c>(published_at, seq)</c>
    /// index; <c>0</c> when nothing is unpublished (design.md §7's "asserted
    /// at an exact value against a clock-aged real row, then at 0 after the
    /// relay drains").
    /// </summary>
    private async Task RecordOutboxLagAsync(CancellationToken cancellationToken)
    {
        var oldestCreatedAt = await db.OutboxMessages
            .Where(row => row.PublishedAt == null)
            .OrderBy(row => row.Seq)
            .Select(row => (DateTime?)row.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var lagMs = oldestCreatedAt is { } createdAt
            ? Math.Max(0d, (clock.UtcNow.UtcDateTime - createdAt).TotalMilliseconds)
            : 0d;

        OtcMetrics.OutboxLagMs.Record(lagMs);
    }

    private static PublishableFact BuildPublishableFact(OutboxMessage row)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-event-type"] = row.EventType,
            ["content-type"] = "application/json",
        };

        // OR4/design.md §5.3, ledger L22/L24 — restore the WRITING
        // command's own trace (row.TraceParent, null with no active span at
        // write time — never fabricated), start a CHILD "outbox.publish"
        // span under it, and inject THAT span's own id as the header the
        // consumer will extract. No stored parent means no span and no
        // header, matching the write side's own "no active span, no
        // traceparent" rule rather than inventing a root trace here.
        var parentContext = TraceContext.ContextFromTraceParent(row.TraceParent);
        using var activity = parentContext is { } parent
            ? OtcActivity.Source.StartActivity("outbox.publish", ActivityKind.Producer, parentContext: parent)
            : null;

        if (activity?.Id is { } traceParent)
        {
            headers["traceparent"] = traceParent;
        }

        // R15: correlationId, Guid.ToString() — the default "D" format,
        // lowercase and hyphenated, matching every golden envelope.
        return new PublishableFact(
            Key: row.CorrelationId.ToString(),
            EnvelopeJson: OutboxEnvelopeMapper.ToWireBytes(row),
            Headers: headers);
    }
}
