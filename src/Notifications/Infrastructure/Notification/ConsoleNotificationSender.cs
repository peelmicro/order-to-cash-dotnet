using Microsoft.Extensions.Logging;
using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.Infrastructure.Notification;

/// <summary>
/// The default, no-I/O <see cref="INotificationSender"/> adapter — bound
/// explicitly by every automated test (feature 23's acceptance bullet 2:
/// "console adapter used in tests via the same port") and by
/// <c>NotificationsOptions.SenderKind</c>'s own default. Logs one structured
/// line per message — <c>to</c>, <c>subject</c> and <c>messageId</c> only,
/// never the body: the body can carry a business reference (payment.received.v1's
/// <c>paymentReference</c>) that has no reason to sit in structured logs.
/// </summary>
public sealed class ConsoleNotificationSender(ILogger<ConsoleNotificationSender> logger) : INotificationSender
{
    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "notification (console adapter): to={To} subject={Subject} messageId={MessageId}",
            message.To,
            message.Subject,
            message.MessageId);

        return Task.CompletedTask;
    }
}
