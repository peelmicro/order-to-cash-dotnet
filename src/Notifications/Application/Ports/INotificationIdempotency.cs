namespace OrderToCash.Notifications.Application.Ports;

/// <summary>Mirrors <c>OrderToCash.Notifications.Infrastructure.Messaging.ConsumptionOutcome</c> without requiring <see cref="NotificationDispatchService"/> to reference that Infrastructure enum directly — the same reasoning as Orders' <c>IdempotentSagaRunOutcome</c>.</summary>
public enum NotificationIdempotencyOutcome
{
    Processed,
    Duplicate,
}

/// <summary>
/// A thin seam over the EXISTING, UNMODIFIED canonical
/// <c>OrderToCash.Notifications.Infrastructure.Messaging.IdempotentConsumer</c>
/// — fixed to <see cref="ConsumerName.Notifications"/>, so
/// <see cref="NotificationDispatchService"/> depends on a fakeable port
/// rather than a concrete class whose dedup insert requires a real
/// <c>DbContext</c>. Mirrors Orders' <c>IIdempotentSagaRunner</c> exactly,
/// with one deliberate difference this service's own divergence requires:
/// <see cref="DeleteRecordAsync"/>. Orders' canonical pattern records the
/// dedup row and the handler's effects in ONE transaction (R17's "same
/// transaction" clause) because both are database writes; sending an email
/// is not a database write, so this service records the row FIRST (as its
/// own short transaction, via <see cref="RecordAsync"/> with the canonical's
/// <c>work</c> left a no-op) and only afterwards calls the bound
/// <c>INotificationSender</c>. If that send throws, the caller uses
/// <see cref="DeleteRecordAsync"/> to remove the just-committed row before
/// rethrowing, so the fact is redelivered as if never recorded — "possibly
/// lose one email on a crash" rather than "duplicate an email", the
/// direction domain-model.md §6's "one delivery" points. See
/// <c>NotificationDispatchService</c>'s own remarks for the full reasoning.
/// </summary>
public interface INotificationIdempotency
{
    /// <summary>Inserts the <c>(eventId, "notifications")</c> dedup row. <see cref="NotificationIdempotencyOutcome.Duplicate"/> means the row already existed and nothing was written.</summary>
    Task<NotificationIdempotencyOutcome> RecordAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>Deletes the row <see cref="RecordAsync"/> just inserted — used ONLY on a failed send, never on success, never on a duplicate.</summary>
    Task DeleteRecordAsync(Guid eventId, CancellationToken cancellationToken);
}
