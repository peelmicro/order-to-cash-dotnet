using MimeKit;

namespace OrderToCash.Notifications.Infrastructure.Notification;

/// <summary>
/// A thin seam over MailKit's <c>IMailTransport</c> — narrowed to exactly
/// the three calls <see cref="MailKitNotificationSender"/> makes, so a test
/// can fake the SMTP round-trip without hand-implementing MailKit's full
/// (~20-member) transport interface. The one real implementation,
/// <see cref="MailKitSmtpTransport"/>, wraps <c>MailKit.Net.Smtp.SmtpClient</c>
/// verbatim.
/// </summary>
public interface ISmtpTransport : IAsyncDisposable
{
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken);

    Task SendAsync(MimeMessage message, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}
