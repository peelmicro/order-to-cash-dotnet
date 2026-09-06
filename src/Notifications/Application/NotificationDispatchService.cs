using Microsoft.Extensions.Logging;
using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.Application;

/// <summary>
/// The ONE generic dispatch unit every fact <c>ICommandHandler</c>
/// (Commands/NotifyFactCommandHandlers.cs) delegates to — mirrors Orders'
/// <c>SagaFactHandler</c>'s composition of the same canonical
/// <c>IdempotentConsumer</c>, through <see cref="INotificationIdempotency"/>,
/// its thin fakeable seam.
/// </summary>
/// <remarks>
/// <b>Insert-first, then send, then delete-on-throw — R17's divergence, and
/// why.</b> Orders_aggregate's canonical pattern records the dedup row and
/// the handler's effects inside ONE transaction, because both are database
/// writes (R17's own "same transaction" clause). Sending an email is not a
/// database write and cannot be enrolled in that transaction — flagged as an
/// open question for this feature by outbox_and_idempotency/design.md §6.3
/// ("Notifications ... must choose between recording before the send and
/// recording after it, and argue the failure mode it accepts"). This method
/// chooses record-first: <see cref="INotificationIdempotency.RecordAsync"/>
/// commits the dedup row as its OWN short transaction (the canonical
/// <c>IdempotentConsumer</c>'s <c>work</c> delegate is a no-op here — the
/// INSERT alone is the whole effect), and only afterwards is the message
/// built and handed to the bound <c>INotificationSender</c>, deliberately
/// OUTSIDE any database transaction. If the send throws,
/// <see cref="INotificationIdempotency.DeleteRecordAsync"/> removes the
/// just-committed row before this method rethrows, so Kafka's redelivery
/// (the fact-consumer BackgroundService never stores the offset for a thrown
/// handler) gets a genuinely fresh attempt rather than a permanently
/// swallowed one. Net effect, stated plainly: a crash strictly BETWEEN the
/// insert committing and the send being attempted turns into "possibly lose
/// one email", never "duplicate an email" — the direction domain-model.md
/// §6's "one delivery per (eventId, consumer)" points, and the opposite of
/// marking-after-send, which would let a redelivery after a successful send
/// re-run the SMTP call every time the offset commit lagged.
///
/// <b>A failing compensation must never mask the original send error.</b> If
/// <see cref="INotificationIdempotency.DeleteRecordAsync"/> itself throws —
/// e.g. the database is briefly unreachable at exactly the moment SMTP
/// failed — that failure is caught HERE, logged with the identifiers needed
/// to clear the orphaned row by hand, and swallowed; the ORIGINAL send
/// exception is always what this method rethrows. Letting a database error
/// replace the real SMTP cause would send whoever debugs the incident to the
/// wrong subsystem.
/// </remarks>
public sealed class NotificationDispatchService(
    INotificationIdempotency idempotency,
    INotificationSender sender,
    ILogger<NotificationDispatchService> logger)
{
    public async Task<NotificationIdempotencyOutcome> DispatchAsync(
        Guid eventId,
        Func<NotificationMessage> buildMessage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buildMessage);

        var outcome = await idempotency.RecordAsync(eventId, cancellationToken).ConfigureAwait(false);

        if (outcome == NotificationIdempotencyOutcome.Duplicate)
        {
            // R18 — acknowledged with no mutation, no second ledger row, and
            // (this service's own rendering of R18) no send. buildMessage is
            // deliberately never invoked on this path — a redelivered fact
            // costs nothing beyond the ledger's own duplicate-key check.
            return outcome;
        }

        var message = buildMessage() with { MessageId = $"{eventId}@order-to-cash" };

        try
        {
            await sender.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception sendError)
        {
            try
            {
                await idempotency.DeleteRecordAsync(eventId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception compensationError)
            {
                logger.LogError(
                    compensationError,
                    "notification-dispatch: compensating delete failed for eventId {EventId} after a failed send " +
                    "({SendError}) — the ledger row is orphaned and must be cleared by hand.",
                    eventId,
                    sendError.Message);
            }

            throw;
        }

        return outcome;
    }
}
