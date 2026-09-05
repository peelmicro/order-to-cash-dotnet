// COPY OF — src/Orders/Infrastructure/Outbox/OutboxRelay.cs
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderToCash.Fulfillment.Application.Ports;
using OrderToCash.Fulfillment.Infrastructure.Persistence;
using OrderToCash.Fulfillment.Infrastructure.Persistence.Entities;

namespace OrderToCash.Fulfillment.Infrastructure.Outbox;

/// <summary>Claimed vs. actually stamped, so a caller (and a test) can tell a "nothing to do" cycle from a "claimed but the publish failed" one.</summary>
public sealed record OutboxRelayResult(int Claimed, int Published);

/// <summary>
/// The one member <see cref="OutboxRelayBackgroundService"/> depends on —
/// resolved from DI rather than the concrete <see cref="OutboxRelay"/> class,
/// so a test can substitute a controllable fake.
/// </summary>
public interface IOutboxRelay
{
    Task<OutboxRelayResult> RunOnceAsync(CancellationToken cancellationToken);
}

/// <summary>
/// <c>RunOnceAsync()</c> = claim -&gt; publish -&gt; stamp, in ONE write-model
/// transaction. A plain class — no base type, no attribute — deriving from
/// nothing, so it is directly callable from a test with no host.
/// </summary>
public sealed class OutboxRelay(
    FulfillmentDbContext db,
    IFactPublisher publisher,
    IClock clock,
    IOptions<OutboxRelayOptions> options,
    ILogger<OutboxRelay> logger) : IOutboxRelay
{
    /// <summary>
    /// The claim's column list, in the order <c>OutboxMessageConfiguration</c>
    /// declares them — every mapped column of <see cref="OutboxMessage"/>,
    /// because <c>FromSqlInterpolated</c> requires ALL of them in the
    /// projection. Exposed so a projection test can compare this list
    /// against the <c>IEntityType</c>'s mapped properties mechanically.
    /// </summary>
    public static readonly IReadOnlyList<string> ClaimColumnNames =
    [
        "id", "event_id", "event_type", "aggregate_id", "correlation_id", "causation_id",
        "payload", "occurred_at", "published_at", "created_at", "seq", "trace_parent",
    ];

    public async Task<OutboxRelayResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var relayOptions = options.Value;
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // The relay's own transaction — not IUnitOfWork: the relay
            // writes no aggregate and enlists no other collaborator, and the
            // claim's table hints need the isolation level pinned at the
            // point they are taken.
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

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

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(relayOptions.PublishTimeoutMs);

            try
            {
                await publisher.PublishAsync(facts, timeoutCts.Token);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                logger.LogError(
                    ex,
                    "Outbox relay failed to publish a batch of {Count} record(s): {EventIds}",
                    claimed.Count,
                    string.Join(",", claimed.Select(row => row.EventId)));

                await transaction.RollbackAsync(cancellationToken);
                return new OutboxRelayResult(claimed.Count, 0);
            }

            var ids = claimed.Select(row => row.Id).ToList();
            var now = clock.UtcNow.UtcDateTime;

            var stamped = await db.OutboxMessages
                .Where(row => ids.Contains(row.Id))
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PublishedAt, now), cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new OutboxRelayResult(claimed.Count, stamped);
        });
    }

    private static PublishableFact BuildPublishableFact(OutboxMessage row) => new(
        // R15: correlationId, Guid.ToString() — the default "D" format,
        // lowercase and hyphenated.
        Key: row.CorrelationId.ToString(),
        EnvelopeJson: OutboxEnvelopeMapper.ToWireBytes(row),
        Headers: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-event-type"] = row.EventType,
            ["content-type"] = "application/json",
        });
}
