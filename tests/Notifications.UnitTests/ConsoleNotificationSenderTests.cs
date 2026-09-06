using Microsoft.Extensions.Logging.Abstractions;
using OrderToCash.Notifications.Application.Ports;
using OrderToCash.Notifications.Infrastructure.Notification;
using Xunit;

namespace OrderToCash.Notifications.UnitTests;

/// <summary>Acceptance bullet 2 — the console adapter reachable through the SAME <see cref="INotificationSender"/> port as the MailKit adapter, no I/O, no throw.</summary>
public sealed class ConsoleNotificationSenderTests
{
    [Fact]
    public async Task SendAsync_CompletesWithoutThrowing_ForAnyWellFormedMessage()
    {
        INotificationSender sender = new ConsoleNotificationSender(NullLogger<ConsoleNotificationSender>.Instance);
        var message = new NotificationMessage("to@example.com", "subject", "text", "<p>html</p>", "event-1@order-to-cash");

        var exception = await Record.ExceptionAsync(() => sender.SendAsync(message, CancellationToken.None));

        Assert.Null(exception);
    }
}
