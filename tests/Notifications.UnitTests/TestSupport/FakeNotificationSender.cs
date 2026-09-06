using OrderToCash.Notifications.Application.Ports;

namespace OrderToCash.Notifications.UnitTests.TestSupport;

/// <summary>A genuine <see cref="INotificationSender"/> fake with a real call record — never a mock.</summary>
public sealed class FakeNotificationSender : INotificationSender
{
    public List<NotificationMessage> SentMessages { get; } = [];

    /// <summary>When set, <see cref="SendAsync"/> throws this instead of recording the send — the send-failure probe.</summary>
    public Exception? ThrowOnSend { get; set; }

    public Task SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        if (ThrowOnSend is { } exception)
        {
            throw exception;
        }

        SentMessages.Add(message);
        return Task.CompletedTask;
    }
}
