namespace OrderToCash.Notifications.Application.Ports;

/// <summary>
/// The rendered notification, provider-neutral — built by one of the seven
/// <c>Infrastructure/Templates/*Template</c> classes from a fact envelope,
/// then handed to whichever <see cref="INotificationSender"/> is bound
/// (console in every automated test, MailKit-over-Mailpit for the real
/// host — feature 23's acceptance bullet 2).
/// </summary>
/// <param name="To">The recipient address, synthesised from a business identifier (domain-model.md carries no email address for any party — this is a demo affordance, not an address book).</param>
/// <param name="Subject">Carries the fact's own summary AND the order's <c>correlationId</c> (feature 23's acceptance list).</param>
/// <param name="Text">The plain-text body.</param>
/// <param name="Html">The HTML body — every payload-derived value inside it is escaped (<c>NotificationFormat.EscapeHtml</c>) before interpolation.</param>
/// <param name="MessageId">
/// Set once, centrally, by <see cref="NotificationDispatchService"/> from the
/// fact's own <c>eventId</c> — <c>&lt;eventId&gt;@order-to-cash</c>. Defence
/// in depth only, never the dedup mechanism (that is the durable
/// <c>processed_events</c> ledger): it makes every send individually
/// attributable in the Mailpit inbox, and any duplicate visible to a human
/// at a glance, but it prevents nothing by itself.
/// </param>
public sealed record NotificationMessage(string To, string Subject, string Text, string Html, string? MessageId = null);

/// <summary>
/// The notification-sender port (feature 23's seam) — ONE interface, TWO
/// adapters (<c>ConsoleNotificationSender</c>, <c>MailKitNotificationSender</c>),
/// swapped by a single binding in <c>NotificationsServiceCollectionExtensions</c>.
/// Every automated test binds the console adapter through this SAME port —
/// never a different seam — which is what "console adapter used in tests via
/// the same port" (acceptance bullet 2) actually requires: a test that
/// bypassed this interface would prove nothing about the real wiring.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(NotificationMessage message, CancellationToken cancellationToken);
}
