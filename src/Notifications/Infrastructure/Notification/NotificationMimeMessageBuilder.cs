using MimeKit;
using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.Infrastructure.Notification;

/// <summary>
/// Builds a <see cref="MimeMessage"/> from a provider-neutral
/// <see cref="NotificationMessage"/> — pure (no I/O), split out of
/// <see cref="MailKitNotificationSender"/> so the MIME shape (both a text
/// and an HTML body, the <c>Message-Id</c> header) is unit-testable without
/// a network call or a fake transport.
/// </summary>
public static class NotificationMimeMessageBuilder
{
    public static MimeMessage Build(NotificationMessage message, string fromAddress)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(fromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;

        if (message.MessageId is { Length: > 0 } messageId)
        {
            mime.MessageId = messageId;
        }

        mime.Body = new BodyBuilder
        {
            TextBody = message.Text,
            HtmlBody = message.Html,
        }.ToMessageBody();

        return mime;
    }
}
