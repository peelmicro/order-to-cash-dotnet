using Microsoft.Extensions.Options;
using MimeKit;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure;
using OrderToCash.Notifications.Infrastructure.Notification;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>
/// The MailKit adapter, tested against a fake <see cref="ISmtpTransport"/> —
/// no real socket, real DNS or real Mailpit container is ever touched by
/// this suite. Live Mailpit verification is separate, recorded in
/// <c>progress/impl_notifications_service.md</c>.
/// </summary>
public sealed class MailKitNotificationSenderTests
{
    [Fact]
    public async Task SendAsync_ConnectsSendsAndDisconnects_InThatOrder()
    {
        var transport = new FakeSmtpTransport();
        var options = Options.Create(new NotificationsSmtpOptions { Host = "mailpit-test-host", Port = 2025, FromAddress = "no-reply@order-to-cash.example" });
        var sender = new MailKitNotificationSender(options, () => transport);
        var message = new NotificationMessage("to@example.com", "subject", "text", "<p>html</p>", "event-1@order-to-cash");

        await sender.SendAsync(message, CancellationToken.None);

        Assert.Equal(["connect", "send", "disconnect", "dispose"], transport.Calls);
        Assert.Equal("mailpit-test-host", transport.ConnectedHost);
        Assert.Equal(2025, transport.ConnectedPort);
        Assert.NotNull(transport.SentMessage);
        Assert.Equal("subject", transport.SentMessage!.Subject);
        Assert.Equal("event-1@order-to-cash", transport.SentMessage.MessageId);
    }

    [Fact]
    public async Task SendAsync_WhenSendThrows_StillDisconnectsAndDisposesBeforeRethrowing()
    {
        var transport = new FakeSmtpTransport { ThrowOnSend = new InvalidOperationException("mailpit unreachable") };
        var options = Options.Create(new NotificationsSmtpOptions());
        var sender = new MailKitNotificationSender(options, () => transport);
        var message = new NotificationMessage("to@example.com", "subject", "text", "<p>html</p>");

        var thrown = await Record.ExceptionAsync(() => sender.SendAsync(message, CancellationToken.None));

        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(["connect", "send", "disconnect", "dispose"], transport.Calls);
    }

    private sealed class FakeSmtpTransport : ISmtpTransport
    {
        public List<string> Calls { get; } = [];

        public string? ConnectedHost { get; private set; }

        public int ConnectedPort { get; private set; }

        public MimeMessage? SentMessage { get; private set; }

        public Exception? ThrowOnSend { get; set; }

        public Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            Calls.Add("connect");
            ConnectedHost = host;
            ConnectedPort = port;
            return Task.CompletedTask;
        }

        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            Calls.Add("send");

            if (ThrowOnSend is { } exception)
            {
                throw exception;
            }

            SentMessage = message;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            Calls.Add("disconnect");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Calls.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }
}
