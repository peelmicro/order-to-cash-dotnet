using Microsoft.Extensions.Logging;
using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.Infrastructure.Notification;

/// <summary>
/// Backlog id 73 — ported from #7's
/// <c>apps/notifications/src/infrastructure/notification/degrading-notification-sender.ts:1-33</c>.
/// Wraps the real <paramref name="inner"/> sender (<c>MailKitNotificationSender</c>)
/// with <paramref name="fallback"/> (<c>ConsoleNotificationSender</c>),
/// wired ONLY when SMTP is configured
/// (<c>NotificationsServiceCollectionExtensions</c>'s <c>Smtp</c> case) — a
/// console-only binding stays unwrapped, exactly #7's own
/// <c>app.module.ts</c> rule ("nothing to degrade from").
///
/// On EVERY send it delegates to <paramref name="inner"/> and, only on
/// failure, asks <see cref="SendFailureClassifier"/> whether retrying could
/// ever succeed:
///
///   - <see cref="SendFailureClassification.Transient"/> — rethrows the
///     ORIGINAL exception UNCHANGED (<see langword="throw"/>, preserving
///     both the instance and its stack trace — #7's own "must not change"
///     half). Every caller above this
///     (<c>NotificationDispatchService.DispatchAsync</c>'s compensating
///     delete, <c>FactRetryDispatcher</c>'s retry-then-DLQ) behaves EXACTLY
///     as it does today.
///   - <see cref="SendFailureClassification.Permanent"/> — logs ONE loud,
///     structured error line naming the cause, renders the SAME message via
///     <paramref name="fallback"/>, and returns NORMALLY (no throw). A
///     caller that never sees a throw never retries and never
///     dead-letters — the fact is acknowledged
///     (<c>NotificationDispatchService</c>'s ledger row stays committed,
///     the Kafka offset commits) exactly as if the send had genuinely
///     succeeded.
///
/// The logged line's <c>correlationId</c> and <c>traceId</c> are NOT added
/// by this class — unlike #7's hand-rolled
/// <c>console.error(JSON.stringify({ ..., traceId }))</c>, #8's ambient
/// <see cref="ILogger"/> pipeline supplies both for free whenever this call
/// happens inside <c>NotificationFactsConsumer.HandleMessageAsync</c>'s own
/// scope: <c>correlationId</c> via its <c>logger.BeginScope</c>
/// (design.md §6's scope-push table), <c>traceId</c> via
/// <c>NotificationsHost.CreateBuilder</c>'s
/// <c>ActivityTrackingOptions.TraceId</c> — the SAME mechanism
/// <c>LogCorrelationTests</c> already proves for every other log line this
/// service emits while handling a fact. Proven for THIS log line by
/// <c>NotificationDegradesOnPermanentFailureTests</c>.
/// </summary>
public sealed class DegradingNotificationSender(
    INotificationSender inner,
    INotificationSender fallback,
    ILogger<DegradingNotificationSender> logger) : INotificationSender
{
    /// <summary>
    /// Review round 1, A2 — internal-only, visible to
    /// <c>OrderToCash.Notifications.UnitTests</c> via
    /// <c>InternalsVisibleTo.cs</c>, so a wiring test can tell WHICH sender
    /// this decorator wraps rather than only that some
    /// <see cref="DegradingNotificationSender"/> was resolved. Two real
    /// sibling senders swapped here (the decorator aimed at the wrong
    /// target) left <c>Notifications.UnitTests</c> green with no accessor
    /// to catch it — this property, plus
    /// <c>NotificationSenderBindingTests</c>, closes that.
    /// </summary>
    internal INotificationSender Inner { get; } = inner;

    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await Inner.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (SendFailureClassifier.Classify(error) == SendFailureClassification.Transient)
            {
                // The "must not change" branch — deleting this rethrow
                // (degrading on EVERY failure, not only genuinely permanent
                // ones) is the arm this class's own guard test targets
                // (progress/impl_notification_send_degrades_on_permanent_failure.md).
                throw;
            }

            // Permanent — loud and structural, on purpose: a reader of the
            // logs must be able to tell "we are not sending email right
            // now, and here is why" without inspecting a DLQ. `Event` is a
            // stable field name so a log-based alert/metric can match on it
            // without parsing `Message` (#7's own reasoning,
            // degrading-notification-sender.ts:83-87).
            logger.LogError(
                error,
                "notifications: email delivery degraded — permanent send failure, will NOT retry or dead-letter; " +
                "rendering to console and acknowledging the fact. event={Event} to={To} subject={Subject} messageId={MessageId}",
                "notification.send.degraded",
                message.To,
                message.Subject,
                message.MessageId);

            await fallback.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }
}
