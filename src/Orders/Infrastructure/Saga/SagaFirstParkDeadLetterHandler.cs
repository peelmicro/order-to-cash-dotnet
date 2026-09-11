using System.Text.Json;
using Microsoft.Extensions.Logging;
using OrderToCash.Orders.Application.Ports;
using OrderToCash.Orders.Application.Sagas;
using OrderToCash.SharedKernel;

namespace OrderToCash.Orders.Infrastructure.Saga;

/// <summary>
/// The one implementation of <see cref="ISagaFirstParkDeadLetterHandler"/> —
/// feature <c>observability_reliability</c>, design.md §4.4 (<c>OR3</c>,
/// <c>R29</c>'s dead-letter clause). Two steps, deliberately in THIS order
/// — the reverse of #7's own window, stated deliberately (ledger L17):
/// <list type="number">
/// <item>
/// <b>In one transaction</b> (<see cref="IUnitOfWork.ExecuteAsync{T}"/>):
/// <see cref="ISagaCommandStore.TryClaimDeadLetterAsync"/>; if it won, load
/// the <see cref="Domain.Order"/>, call
/// <see cref="Domain.Order.RecordSagaFailure"/>, then
/// <see cref="IOrderRepository.SaveChangesAsync"/> — which writes the
/// <c>order.saga_failed.v1</c> outbox row through the EXISTING writer. The
/// claim and the fact commit TOGETHER or not at all.
/// </item>
/// <item>
/// <b>After that commit</b>, publish the triggering envelope to
/// <c>&lt;source topic&gt;.dlq</c> through the SAME <see cref="IDeadLetterPublisher"/>
/// <c>OR1</c> defines — never a second/parallel publisher. Kafka cannot
/// enlist in the MS-SQL transaction, so this step is the one non-atomic
/// step: a crash between 1 and 2 loses the diagnostic <c>.dlq</c> copy
/// while the timeline entry survives (the BETTER direction — <c>R29</c>
/// actually requires the timeline entry; the <c>.dlq</c> copy is
/// <c>asyncapi.yaml</c>'s own words, "purely diagnostic").
/// </item>
/// </list>
/// </summary>
public sealed class SagaFirstParkDeadLetterHandler(
    ISagaCommandStore store,
    IUnitOfWork unitOfWork,
    IOrderRepository orders,
    IDeadLetterPublisher deadLetters,
    IClock clock,
    ILogger<SagaFirstParkDeadLetterHandler> logger) : ISagaFirstParkDeadLetterHandler
{
    public async Task HandleAsync(SagaCommandRecord claimed, int attempts, string lastError, CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow;

        var wonTheDeadLetterClaim = await unitOfWork.ExecuteAsync(
            async ct =>
            {
                var won = await store.TryClaimDeadLetterAsync(claimed.Id, ct).ConfigureAwait(false);

                if (!won)
                {
                    // A later sweep cycle re-parking the SAME row — OR3's
                    // "at most once" claim, guarded by TryClaimDeadLetterAsync's
                    // own single conditional UPDATE. Neither the fact nor the
                    // .dlq publish (below) may happen a second time.
                    return false;
                }

                var order = await orders.GetByIdAsync(UniqueId.From(claimed.OrderId), ct).ConfigureAwait(false);

                if (order is null)
                {
                    // SO8-style residue (a saga command row can never
                    // legitimately precede its own order's row) — log and
                    // move on. This hook must never turn a diagnostic side
                    // effect into a reason the park transition itself fails.
                    logger.LogWarning(
                        "SagaFirstParkDeadLetterHandler: no order row for {OrderId}; order.saga_failed.v1 not recorded (command {Command}).",
                        claimed.OrderId,
                        claimed.Command);

                    return true; // still claimed — the .dlq publish below is still owed.
                }

                order.RecordSagaFailure(
                    SagaCommandKinds.ToToken(claimed.Command),
                    attempts,
                    lastError,
                    occurredAt,
                    UniqueId.From(claimed.TriggeringEventId));

                await orders.SaveChangesAsync(ct).ConfigureAwait(false);

                return true;
            },
            cancellationToken).ConfigureAwait(false);

        if (!wonTheDeadLetterClaim)
        {
            return;
        }

        if (claimed.TriggeringEventEnvelope is not { Length: > 0 } envelopeBytes || string.IsNullOrEmpty(claimed.TriggeringEventTopic))
        {
            // A row enqueued before this feature's columns existed, or one
            // from any other enqueue site that genuinely supplied no
            // envelope (none exists today) — nothing to republish. An
            // operator-cancel compensation row (CancelOrderCommandHandler)
            // is NOT this case since feature
            // operator_note_survives_the_compensation_branches (id 71): it
            // carries the synthetic orders.cancel.requested envelope and IS
            // republished below, on the same path as any other row.
            logger.LogWarning(
                "SagaFirstParkDeadLetterHandler: row {CommandId} carries no triggering envelope; .dlq publish skipped.",
                claimed.Id);

            return;
        }

        var publication = new DeadLetterPublication(
            claimed.TriggeringEventTopic,
            envelopeBytes,
            ConsumerName.OrdersSaga,
            attempts,
            lastError,
            EventTypeOf(envelopeBytes),
            occurredAt,
            occurredAt);

        await deadLetters.PublishAsync(publication, cancellationToken).ConfigureAwait(false);

        logger.LogError(
            "SagaFirstParkDeadLetterHandler: order {OrderId} command {Command} first-parked after {Attempts} attempts ({LastError}) — order.saga_failed.v1 recorded, triggering fact republished to {SourceTopic}.dlq.",
            claimed.OrderId,
            claimed.Command,
            attempts,
            lastError,
            claimed.TriggeringEventTopic);
    }

    /// <summary>
    /// The triggering envelope's OWN <c>eventType</c> — never the claimed
    /// row's <c>Command</c> token, which names the SAGA COMMAND this row
    /// owed, not the fact that owed it. Read straight off the raw
    /// bytes exactly as <c>SagaFactsConsumer</c> does, so no second
    /// authority for "what fact was this" exists.
    /// </summary>
    private static string EventTypeOf(byte[] envelopeBytes)
    {
        using var document = JsonDocument.Parse(envelopeBytes);
        return document.RootElement.TryGetProperty("eventType", out var eventType)
            ? eventType.GetString() ?? "unknown"
            : "unknown";
    }
}
