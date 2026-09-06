using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace OrderToCash.Notifications.Infrastructure.Notification;

/// <summary>
/// The one real <see cref="ISmtpTransport"/> — a thin wrapper over
/// <see cref="SmtpClient"/>. Mailpit needs no TLS and no authentication
/// locally (<c>docker-compose.infra.yml</c>'s <c>mailpit</c> service), so
/// this class deliberately never calls <c>AuthenticateAsync</c> — see
/// <c>NotificationsSmtpOptions</c>'s own header for why that is a scope
/// decision, not an oversight.
/// </summary>
public sealed class MailKitSmtpTransport : ISmtpTransport
{
    private readonly SmtpClient _client = new();

    public Task ConnectAsync(string host, int port, CancellationToken cancellationToken) =>
        _client.ConnectAsync(host, port, SecureSocketOptions.None, cancellationToken);

    public Task SendAsync(MimeMessage message, CancellationToken cancellationToken) =>
        _client.SendAsync(message, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        _client.DisconnectAsync(quit: true, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}
