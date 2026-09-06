using Microsoft.EntityFrameworkCore;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure.Persistence;

namespace OrderToCash.Notifications.Infrastructure.Messaging;

/// <summary>
/// The one implementation of <see cref="INotificationIdempotency"/> —
/// composes the EXISTING, UNMODIFIED canonical <see cref="IdempotentConsumer"/>
/// with <see cref="ConsumerName.Notifications"/> fixed, translating
/// <see cref="ConsumptionOutcome"/> into <see cref="NotificationIdempotencyOutcome"/>
/// (mirrors Orders' <c>IdempotentConsumerSagaRunner</c> exactly). The
/// compensating <see cref="DeleteRecordAsync"/> is this service's OWN code —
/// it does not touch <see cref="IdempotentConsumer"/> or
/// <see cref="ProcessedEventLedger"/> at all, so it carries no obligation
/// toward OI12's byte-identity comparison.
/// </summary>
public sealed class NotificationIdempotency(IdempotentConsumer idempotentConsumer, NotificationsDbContext db) : INotificationIdempotency
{
    public async Task<NotificationIdempotencyOutcome> RecordAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var outcome = await idempotentConsumer.RunOnceAsync(
            eventId,
            ConsumerName.Notifications,
            static _ => Task.CompletedTask, // the INSERT alone is the whole effect — this service's own divergence (NotificationDispatchService's remarks).
            cancellationToken).ConfigureAwait(false);

        return outcome == ConsumptionOutcome.Duplicate ? NotificationIdempotencyOutcome.Duplicate : NotificationIdempotencyOutcome.Processed;
    }

    public async Task DeleteRecordAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var consumerToken = ConsumerNames.ToToken(ConsumerName.Notifications);

        var row = await db.ProcessedEvents
            .SingleOrDefaultAsync(p => p.EventId == eventId && p.Consumer == consumerToken, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            // Nothing to delete — defensive only; RecordAsync always inserts
            // before this is ever called on the same eventId.
            return;
        }

        db.ProcessedEvents.Remove(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
