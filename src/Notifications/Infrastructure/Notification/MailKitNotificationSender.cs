using Microsoft.Extensions.Options;
using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.Infrastructure.Notification;

/// <summary>
/// The real <see cref="INotificationSender"/> adapter — MailKit over SMTP,
/// against Mailpit locally (feature 23's brief: "MailKit → Mailpit"). Bound
/// only when <c>NotificationsOptions.SenderKind</c> is
/// <see cref="NotificationSenderKind.Smtp"/> (<c>Program.cs</c> only — never
/// a test). No test in this repository can send real mail: every automated
/// test binds <see cref="ConsoleNotificationSender"/> through the SAME
/// <see cref="INotificationSender"/> port instead of constructing this class
/// at all.
/// </summary>
public sealed class MailKitNotificationSender(IOptions<NotificationsSmtpOptions> options, Func<ISmtpTransport> transportFactory) : INotificationSender
{
    public async Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        var mime = NotificationMimeMessageBuilder.Build(message, options.Value.FromAddress);

        var transport = transportFactory();
        await using (transport.ConfigureAwait(false))
        {
            await transport.ConnectAsync(options.Value.Host, options.Value.Port, cancellationToken).ConfigureAwait(false);
            try
            {
                await transport.SendAsync(mime, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
