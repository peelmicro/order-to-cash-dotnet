// CANONICAL COPY — feature observability_reliability, design.md §3.2.
// This file is the reference every fact-consuming service's own
// retry-then-dead-letter wrapper copies VERBATIM (OR2,
// tests/Orders.UnitTests/FactRetryDispatcherParityTests.cs), exactly as
// IdempotentConsumer.cs already is. Two regions are normalised for the
// parity check: the leading banner you are reading now (every contiguous
// `//`/`///` line up to the first line that is neither), and the single
// namespace declaration below. Outside those two regions this file must
// never name a service (`Orders`, `Projector`, `Notifications`, in any
// casing) and every `using` must resolve to a namespace on the whitelist -
// System.Diagnostics, Microsoft.Extensions.Logging,
// Microsoft.Extensions.Options, or the copying service's own
// `.Application.Ports`/`.Infrastructure.Observability` namespaces, matched
// by suffix rather than by literal text (which is why `IClock`,
// `IFactRetryDelay`, `IDeadLetterPublisher`, `DeadLetterPublication`,
// `ConsumerName`, `FactStreamMessage` and `OtcMetrics` - per-service files
// at identical paths in every fact-consuming service - may be referenced
// here). `FactRetryOptions` needs no `using` at all: it lives in this same
// file's own namespace in every copy. A copy that fails either constraint
// is not adoptable, and the guard is FactRetryDispatcherParityTests'
// adoptability case.
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderToCash.Projector.Application.Ports;
using OrderToCash.Projector.Infrastructure.Observability;

namespace OrderToCash.Projector.Infrastructure.Messaging;

/// <summary>
/// Retries a fact's processing in line up to
/// <see cref="FactRetryOptions.MaxAttempts"/> with exponential backoff, and
/// on exhaustion publishes the unmodified original envelope bytes to the
/// source topic's <c>.dlq</c> topic and returns NORMALLY — never rethrows on
/// exhaustion — so the caller's own Kafka subscriber loop reaches
/// <c>StoreOffset</c> and the partition is not blocked (OR1, R16, design.md
/// §3.2). <see cref="OperationCanceledException"/> on
/// <paramref name="cancellationToken"/>-driven shutdown is rethrown
/// UNCONDITIONALLY — a host shutdown is not a poison message (ledger L13).
/// </summary>
public sealed class FactRetryDispatcher(
    IClock clock,
    IFactRetryDelay delay,
    IDeadLetterPublisher deadLetters,
    IOptions<FactRetryOptions> options,
    ILogger<FactRetryDispatcher> logger)
{
    public async Task DispatchAsync(
        string sourceTopic,
        FactStreamMessage message,
        Guid eventId,
        string eventType,
        Guid correlationId,
        ConsumerName consumer,
        Func<CancellationToken, Task> process,
        CancellationToken cancellationToken)
    {
        // Captured ONCE, at the top of this call — never re-read at the
        // end. The dead-letter's x-attempts header (below) is built from
        // the LOOP's own counter, never from a fresh read of this value:
        // the two coincide on every ordinary run, but only the loop's own
        // count is provenance (ledger L19).
        var maxAttempts = options.Value.MaxAttempts;
        var backoffMs = options.Value.BackoffMs;

        // otc_fact_processing_latency_ms (OR5/design.md §7) — entry to
        // exit of this ONE call, recorded on the success path AND on the
        // exhausted-retry/DLQ path below, never only on success. Read
        // from the injected IClock, not a bare Stopwatch, so a unit
        // test's fake clock controls the recorded VALUE exactly — the
        // same discipline #7's fact-retry-dispatcher.ts:130-135 uses
        // (an injected Clock, not Date.now()), and every other
        // timestamp in this method already follows here (review round 3, D8).
        var enteredAt = clock.UtcNow;

        Exception? lastFailure = null;
        DateTimeOffset? firstFailedAt = null;
        var attempt = 0;

        for (attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await process(cancellationToken).ConfigureAwait(false);
                OtcMetrics.FactProcessingLatencyMs.Record((clock.UtcNow - enteredAt).TotalMilliseconds, new KeyValuePair<string, object?>("consumer", consumer.ToString()));
                return;
            }
            catch (OperationCanceledException)
            {
                // A host shutdown, never a poison message — rethrown
                // unconditionally, never retried, never dead-lettered
                // (ledger L13).
                throw;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                firstFailedAt ??= clock.UtcNow;

                logger.LogWarning(
                    ex,
                    "Fact {EventType} ({EventId}, correlation {CorrelationId}) failed on attempt {Attempt}/{MaxAttempts} for consumer {Consumer}: {Message}",
                    eventType,
                    eventId,
                    correlationId,
                    attempt,
                    maxAttempts,
                    consumer,
                    ex.Message);

                if (attempt < maxAttempts)
                {
                    await delay.DelayAsync(backoffMs << (attempt - 1), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // Exhausted. The for loop's own increment leaves `attempt` at
        // maxAttempts + 1 — `attempt - 1` is the loop's own count of
        // ACTUAL process() invocations, the value the x-attempts header
        // carries (never options.Value.MaxAttempts re-read here).
        var attemptsMade = attempt - 1;
        var failedAt = clock.UtcNow;

        var publication = new DeadLetterPublication(
            sourceTopic,
            message.Value,
            consumer,
            attemptsMade,
            lastFailure?.Message ?? "unknown error",
            eventType,
            firstFailedAt ?? failedAt,
            failedAt);

        await deadLetters.PublishAsync(publication, cancellationToken).ConfigureAwait(false);

        OtcMetrics.FactProcessingLatencyMs.Record((clock.UtcNow - enteredAt).TotalMilliseconds, new KeyValuePair<string, object?>("consumer", consumer.ToString()));

        logger.LogError(
            lastFailure,
            "Fact {EventType} ({EventId}, correlation {CorrelationId}) dead-lettered after {Attempts} attempts for consumer {Consumer} — published to {SourceTopic}.dlq.",
            eventType,
            eventId,
            correlationId,
            attemptsMade,
            consumer,
            sourceTopic);
    }
}
